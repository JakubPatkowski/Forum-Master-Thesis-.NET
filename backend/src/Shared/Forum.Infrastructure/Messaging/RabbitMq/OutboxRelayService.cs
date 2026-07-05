using System.Globalization;
using System.Text;

using Forum.Infrastructure.Messaging.Outbox;
using Forum.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;

namespace Forum.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// The transactional-outbox relay (ADR 0009), registered once per module. Polls the module's
/// <c>outbox_messages</c> for unprocessed rows and publishes each to the module's durable topic exchange
/// (routing key = the event's short type name), stamping <c>ProcessedOnUtc</c> — or recording the failure on
/// <c>Error</c> and backing off exponentially. Rows are claimed with <c>FOR UPDATE SKIP LOCKED</c>, so multiple
/// API replicas relay disjoint batches concurrently and a crashed replica's claim dies with its transaction —
/// the standard polling-publisher shape (the Files advisory lock is for a singleton job, a different problem).
/// Publisher confirms are enabled: a row is only stamped processed after the broker confirmed the publish, which
/// makes delivery at-least-once; consumers dedupe by EventId.
/// </summary>
internal sealed class OutboxRelayService<TContext> : BackgroundService
    where TContext : ForumDbContext
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqConnection _connection;
    private readonly ModuleMessagingOptions<TContext> _module;
    private readonly MessagingOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<OutboxRelayService<TContext>> _logger;
    private IChannel? _channel;

    public OutboxRelayService(
        IServiceScopeFactory scopeFactory,
        IRabbitMqConnection connection,
        ModuleMessagingOptions<TContext> module,
        IOptions<MessagingOptions> options,
        TimeProvider time,
        ILogger<OutboxRelayService<TContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _connection = connection;
        _module = module;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveFailures = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int published;
                try
                {
                    published = await PublishPendingAsync(stoppingToken);
                    consecutiveFailures = 0;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Broker or database trouble: the rows stay unprocessed and are retried after a backoff.
                    consecutiveFailures++;
                    var backoff = BackoffDelay(consecutiveFailures);
                    _logger.LogWarning(
                        exception,
                        "Outbox relay for '{Module}' failed; retrying in {Delay}.", _module.ModuleName, backoff);
                    await Task.Delay(backoff, stoppingToken);
                    continue;
                }

                // A full batch hints at a backlog — drain it before sleeping a poll interval.
                if (published < _options.BatchSize)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(_options.PollIntervalMilliseconds), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        finally
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
            }
        }
    }

    private async Task<int> PublishPendingAsync(CancellationToken cancellationToken)
    {
        // Fail fast on a dead broker, before touching the database.
        var channel = await GetChannelAsync(cancellationToken);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        var table = db.Model.FindEntityType(typeof(OutboxMessage))!.GetSchemaQualifiedTableName();
        var claimSql = string.Create(
            CultureInfo.InvariantCulture,
            $"SELECT * FROM {table} WHERE processed_on_utc IS NULL ORDER BY id LIMIT {_options.BatchSize} FOR UPDATE SKIP LOCKED");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw(claimSql)
            .AsTracking()
            .ToListAsync(cancellationToken);
        if (batch.Count == 0)
        {
            return 0;
        }

        Exception? publishFailure = null;
        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                var properties = new BasicProperties
                {
                    MessageId = message.Id.ToString(),
                    Type = message.Type,
                    CorrelationId = message.CorrelationId,
                    ContentType = "application/json",
                    Persistent = true,
                };
                await channel.BasicPublishAsync(
                    _module.ModuleName, ShortTypeName(message.Type), mandatory: false, properties,
                    Encoding.UTF8.GetBytes(message.Payload), cancellationToken);

                message.ProcessedOnUtc = _time.GetUtcNow();
                message.Error = null;
                published++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Record the failure on the row and stop the pass — the rest of the batch would hit the same broker.
                message.Error = Truncate(exception.Message, 2048);
                publishFailure = exception;
                break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return publishFailure is null ? published : throw publishFailure;
    }

    private async ValueTask<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        var connection = await _connection.GetConnectionAsync(cancellationToken);

        // Publisher confirms: BasicPublishAsync only completes once the broker took responsibility for the message.
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await channel.ExchangeDeclareAsync(
            _module.ModuleName, ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: cancellationToken);

        return _channel = channel;
    }

    private TimeSpan BackoffDelay(int consecutiveFailures)
    {
        var exponential = _options.PollIntervalMilliseconds * Math.Pow(2, Math.Min(consecutiveFailures, 10));
        return TimeSpan.FromMilliseconds(Math.Min(exponential, _options.MaxPublishBackoffMilliseconds));
    }

    private static string ShortTypeName(string fullTypeName)
    {
        var lastDot = fullTypeName.LastIndexOf('.');
        return lastDot < 0 ? fullTypeName : fullTypeName[(lastDot + 1)..];
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
