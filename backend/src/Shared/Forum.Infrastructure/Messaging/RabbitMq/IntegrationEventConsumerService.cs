using System.Reflection;
using System.Text.Json;

using Forum.Common.Correlation;
using Forum.Common.Messaging;
using Forum.Common.Telemetry;
using Forum.Infrastructure.Messaging.Inbox;
using Forum.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Forum.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// Per-module RabbitMQ consumer host. Declares the module's work/retry/poison queues, binds the work queue to
/// exactly the exchange + routing-key pairs the module registered handlers for, and dispatches each delivery
/// through the scoped <see cref="IEventBus"/> — so the existing in-process handlers run unchanged, now fed by
/// the wire. Idempotency is structural: the event's inbox row is inserted first and the handlers' effects join
/// the same database transaction (handlers use the module's scoped DbContext), so a redelivered EventId hits
/// the inbox primary key and is acked without side effects. A failing delivery is rejected into the TTL retry
/// queue and re-enters the work queue after the delay; after <see cref="MessagingOptions.MaxDeliveryAttempts"/>
/// total attempts (or on a malformed payload) it is parked in the poison queue so it can never block the queue.
/// </summary>
internal sealed class IntegrationEventConsumerService<TContext> : BackgroundService
    where TContext : ForumDbContext
{
    private static readonly MethodInfo PublishMethod =
        typeof(IEventBus).GetMethod(nameof(IEventBus.PublishAsync))!;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqConnection _connection;
    private readonly ModuleMessagingOptions<TContext> _module;
    private readonly MessagingOptions _options;
    private readonly TimeProvider _time;
    private readonly ForumMetrics _metrics;
    private readonly string _serviceName;
    private readonly ILogger<IntegrationEventConsumerService<TContext>> _logger;

    /// <summary>Routing key → (event CLR type, closed generic IEventBus.PublishAsync), from the module's bindings.</summary>
    private readonly Dictionary<string, (Type EventType, MethodInfo Publish)> _consumed;

    public IntegrationEventConsumerService(
        IServiceScopeFactory scopeFactory,
        IRabbitMqConnection connection,
        ModuleMessagingOptions<TContext> module,
        IOptions<MessagingOptions> options,
        TimeProvider time,
        ForumMetrics metrics,
        ILogger<IntegrationEventConsumerService<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _connection = connection;
        _module = module;
        _options = options.Value;
        _time = time;
        _metrics = metrics;
        _serviceName = $"consumer-{module.ModuleName}";
        _logger = logger;
        _consumed = module.ConsumedEvents.ToDictionary(
            static consumed => MessagingTopology.RoutingKey(consumed.EventType),
            static consumed => (consumed.EventType, PublishMethod.MakeGenericMethod(consumed.EventType)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Boot tick: makes the age gauge exist even when the broker is unreachable from the start.
        _metrics.HostedServiceTick(_serviceName);

        var queue = MessagingTopology.EventsQueue(_module.ModuleName);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var connection = await _connection.GetConnectionAsync(stoppingToken);
                    await using var channel = await connection.CreateChannelAsync(
                        options: null, cancellationToken: stoppingToken);
                    await DeclareTopologyAsync(channel, stoppingToken);
                    await channel.BasicQosAsync(0, _options.Prefetch, global: false, stoppingToken);

                    var consumer = new AsyncEventingBasicConsumer(channel);
                    consumer.ReceivedAsync += (_, delivery) => OnDeliveredAsync(channel, delivery, stoppingToken);
                    await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Consuming integration events on '{Queue}'.", queue);
                    }

                    while (channel.IsOpen && !stoppingToken.IsCancellationRequested)
                    {
                        _metrics.HostedServiceTick(_serviceName);
                        await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    }
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    // Filtered on the token, not the exception type — a client-internal OperationCanceledException
                    // must trigger a reconnect here rather than silently ending the consumer for good.
                    _logger.LogWarning(
                        exception, "Consumer host for '{Queue}' lost the broker; reconnecting shortly.", queue);
                    await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        var work = MessagingTopology.EventsQueue(_module.ModuleName);
        var retry = MessagingTopology.RetryQueue(_module.ModuleName);
        var poison = MessagingTopology.PoisonQueue(_module.ModuleName);

        // Work → (reject) → retry → (TTL expiry) → work again; the default exchange routes by queue name.
        await channel.QueueDeclareAsync(work, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = retry,
            },
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(retry, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = work,
                ["x-message-ttl"] = _options.RetryDelayMilliseconds,
            },
            cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(poison, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: cancellationToken);

        foreach (var consumed in _module.ConsumedEvents)
        {
            // Declared defensively with the relay's parameters — whichever side boots first creates it.
            await channel.ExchangeDeclareAsync(
                consumed.SourceExchange, ExchangeType.Topic, durable: true, autoDelete: false,
                cancellationToken: cancellationToken);
            await channel.QueueBindAsync(
                work, consumed.SourceExchange, MessagingTopology.RoutingKey(consumed.EventType),
                cancellationToken: cancellationToken);
        }
    }

    private async Task OnDeliveredAsync(
        IChannel channel, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        try
        {
            if (!_consumed.TryGetValue(delivery.RoutingKey, out var descriptor))
            {
                await ParkPoisonAsync(
                    channel, delivery, $"no handler bound for routing key '{delivery.RoutingKey}'", cancellationToken);
                return;
            }

            IIntegrationEvent? integrationEvent;
            try
            {
                integrationEvent = JsonSerializer.Deserialize(
                    delivery.Body.Span, descriptor.EventType, IntegrationEventJson.SerializerOptions) as IIntegrationEvent;
            }
            catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
            {
                integrationEvent = null;
            }

            if (integrationEvent is null)
            {
                await ParkPoisonAsync(channel, delivery, "payload could not be deserialized", cancellationToken);
                return;
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            using var correlationScope = RestoreCorrelation(scope.ServiceProvider, delivery);

            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Inbox first: a duplicate EventId (redelivery, relay retry, another replica) hits the primary key here.
            db.Set<InboxMessage>().Add(new InboxMessage
            {
                Id = integrationEvent.EventId,
                ProcessedOnUtc = _time.GetUtcNow(),
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsDuplicateKey(exception))
            {
                await transaction.RollbackAsync(cancellationToken);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
                _metrics.MessageConsumed(_module.ModuleName, ForumMetrics.ConsumeOutcomeDuplicate);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "Skipped duplicate delivery of event {EventId} on '{Queue}'.",
                        integrationEvent.EventId, MessagingTopology.EventsQueue(_module.ModuleName));
                }

                return;
            }

            // Handlers share the scope's DbContext, so their effects commit atomically with the inbox row.
            var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
            await (Task)descriptor.Publish.Invoke(bus, [integrationEvent, cancellationToken])!;

            await transaction.CommitAsync(cancellationToken);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
            _metrics.MessageConsumed(_module.ModuleName, ForumMetrics.ConsumeOutcomeOk);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown mid-delivery: the unacked message is redelivered and deduped via the inbox.
        }
        catch (Exception exception)
        {
            await RetryOrParkAsync(channel, delivery, exception, cancellationToken);
        }
    }

    private async Task RetryOrParkAsync(
        IChannel channel, BasicDeliverEventArgs delivery, Exception exception, CancellationToken cancellationToken)
    {
        try
        {
            var attempts = DeliveryAttempts(delivery);
            if (attempts >= _options.MaxDeliveryAttempts)
            {
                _logger.LogError(
                    exception,
                    "Delivery '{RoutingKey}' failed on attempt {Attempt}/{Max}; parking in the poison queue.",
                    delivery.RoutingKey, attempts, _options.MaxDeliveryAttempts);
                await ParkPoisonAsync(
                    channel, delivery, $"failed after {attempts} attempts: {exception.Message}", cancellationToken);
                return;
            }

            _logger.LogWarning(
                exception,
                "Delivery '{RoutingKey}' failed on attempt {Attempt}/{Max}; sending to the retry queue.",
                delivery.RoutingKey, attempts, _options.MaxDeliveryAttempts);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
            _metrics.MessageConsumed(_module.ModuleName, ForumMetrics.ConsumeOutcomeRetry);
        }
        catch (Exception brokerException) when (brokerException is not OperationCanceledException)
        {
            // The channel died while reporting the failure; the unacked message is redelivered after reconnect.
            _logger.LogWarning(
                brokerException, "Could not reject delivery '{RoutingKey}'; it will be redelivered.", delivery.RoutingKey);
        }
    }

    private async Task ParkPoisonAsync(
        IChannel channel, BasicDeliverEventArgs delivery, string reason, CancellationToken cancellationToken)
    {
        var properties = new BasicProperties
        {
            ContentType = delivery.BasicProperties.ContentType,
            Type = delivery.BasicProperties.Type,
            MessageId = delivery.BasicProperties.MessageId,
            CorrelationId = delivery.BasicProperties.CorrelationId,
            Persistent = true,
            Headers = new Dictionary<string, object?>
            {
                ["x-poison-reason"] = reason,
                ["x-original-routing-key"] = delivery.RoutingKey,
            },
        };
        await channel.BasicPublishAsync(
            string.Empty, MessagingTopology.PoisonQueue(_module.ModuleName), mandatory: false, properties,
            delivery.Body, cancellationToken);
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        _metrics.MessageConsumed(_module.ModuleName, ForumMetrics.ConsumeOutcomePoison);
        _logger.LogWarning(
            "Parked poison message '{RoutingKey}' ({MessageId}): {Reason}.",
            delivery.RoutingKey, delivery.BasicProperties.MessageId, reason);
    }

    private IDisposable? RestoreCorrelation(IServiceProvider scopedProvider, BasicDeliverEventArgs delivery)
    {
        var correlationId = delivery.BasicProperties.CorrelationId;
        if (string.IsNullOrEmpty(correlationId))
        {
            return null;
        }

        // Restores the originating request's id into the ambient context (so chained outbox writes carry it on)
        // and onto this delivery's log events.
        scopedProvider.GetService<ICorrelationContext>()?.Set(correlationId);
        return _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
    }

    /// <summary>This delivery's ordinal: 1 on first delivery, +1 per prior rejection recorded in <c>x-death</c>.</summary>
    private long DeliveryAttempts(BasicDeliverEventArgs delivery)
    {
        if (delivery.BasicProperties.Headers?.TryGetValue("x-death", out var header) is not true
            || header is not List<object?> deaths)
        {
            return 1;
        }

        var workQueue = MessagingTopology.EventsQueue(_module.ModuleName);
        foreach (var death in deaths)
        {
            if (death is IDictionary<string, object?> entry
                && entry.TryGetValue("queue", out var queue)
                && queue is byte[] queueBytes
                && System.Text.Encoding.UTF8.GetString(queueBytes) == workQueue
                && entry.TryGetValue("count", out var count)
                && count is long rejections)
            {
                return rejections + 1;
            }
        }

        return 1;
    }

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
