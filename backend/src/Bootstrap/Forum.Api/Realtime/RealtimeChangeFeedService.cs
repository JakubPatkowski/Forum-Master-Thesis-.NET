using Forum.Common.Telemetry;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.RabbitMq;

using Microsoft.Extensions.Options;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Forum.Api.Realtime;

/// <summary>
/// The hub's bus consumer (ADR 0010: "the WebSocket fan-out is just another consumer"). Unlike the Phase 6
/// module consumers it binds a <b>server-named, exclusive, auto-delete queue per replica</b> — the module hosts'
/// shared <c>&lt;module&gt;.events</c> queue is deliberately competing-consumers (one handling per event across
/// all replicas), which would silently starve every replica but the race winner of pushes. Here the topic
/// exchanges fan each event into every replica's own queue, so each replica notifies the sockets only it knows
/// about. There is also no DB inbox, retry or poison parking: a push has no persistent effect, duplicates are
/// idempotent for the client (re-fetch), and a dropped delivery self-heals through reconnect-resync — a shared
/// inbox would even re-introduce cross-replica dedupe and break the fan-out.
/// </summary>
internal sealed class RealtimeChangeFeedService : BackgroundService
{
    private readonly IRabbitMqConnection _connection;
    private readonly IRealtimeNotificationSink _sink;
    private readonly MessagingOptions _options;
    private readonly ForumMetrics _metrics;
    private readonly ILogger<RealtimeChangeFeedService> _logger;

    public RealtimeChangeFeedService(
        IRabbitMqConnection connection,
        IRealtimeNotificationSink sink,
        IOptions<MessagingOptions> options,
        ForumMetrics metrics,
        ILogger<RealtimeChangeFeedService> logger)
    {
        _connection = connection;
        _sink = sink;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Boot tick: makes the age gauge exist even when the broker is unreachable from the start.
        _metrics.HostedServiceTick("realtime-feed");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var connection = await _connection.GetConnectionAsync(stoppingToken);
                    await using var channel = await connection.CreateChannelAsync(
                        options: null, cancellationToken: stoppingToken);

                    // Server-named + exclusive + auto-delete: unique to this replica, gone when it disconnects
                    // (nothing accumulates while a replica is down — its clients resync on reconnect anyway).
                    var queue = (await channel.QueueDeclareAsync(
                        queue: string.Empty, durable: false, exclusive: true, autoDelete: true,
                        cancellationToken: stoppingToken)).QueueName;

                    foreach (var eventType in RealtimeEventMap.ConsumedEvents)
                    {
                        // Declared defensively with the relay's parameters — whichever side boots first creates it.
                        var exchange = MessagingTopology.SourceExchange(eventType);
                        await channel.ExchangeDeclareAsync(
                            exchange, ExchangeType.Topic, durable: true, autoDelete: false,
                            cancellationToken: stoppingToken);
                        await channel.QueueBindAsync(
                            queue, exchange, MessagingTopology.RoutingKey(eventType), cancellationToken: stoppingToken);
                    }

                    // autoAck: a push is best-effort by design; there is nothing durable to protect with acks.
                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += (_, delivery) => OnDeliveredAsync(delivery, stoppingToken);
                    await channel.BasicConsumeAsync(queue, autoAck: true, consumer, stoppingToken);
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Realtime hub consuming change events on '{Queue}'.", queue);
                    }

                    while (channel.IsOpen && !stoppingToken.IsCancellationRequested)
                    {
                        _metrics.HostedServiceTick("realtime-feed");
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // Filtered on the token, not the exception type — a client-internal OperationCanceledException
                    // must trigger a reconnect here rather than silently ending the feed for good.
                    _logger.LogWarning(exception, "Realtime hub lost the broker; reconnecting shortly.");
                    await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private async Task OnDeliveredAsync(BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            if (RealtimeEventMap.TryMap(delivery.RoutingKey, delivery.Body.Span, out var notification))
            {
                await _sink.PublishAsync(notification!, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown mid-dispatch; clients resync on reconnect.
        }
        catch (Exception exception)
        {
            // Never let one bad dispatch take the consumer down; the affected clients self-heal via resync.
            _logger.LogWarning(exception, "Failed to dispatch realtime notification '{RoutingKey}'.", delivery.RoutingKey);
        }
    }
}
