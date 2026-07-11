using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Forum.Common.Telemetry;

/// <summary>
/// The domain-level metrics required by REQUIREMENTS §7, exported because the host calls
/// <c>AddMeter(MeterName)</c>. BCL <see cref="System.Diagnostics.Metrics"/> only, so module Application layers
/// record measurements without referencing OpenTelemetry (injected like <c>ICorrelationContext</c>; Domain
/// layers stay free of it). Measurements go through semantic methods instead of exposed instruments so the tag
/// sets stay closed and low-cardinality in ONE place — every distinct tag value becomes a Prometheus series,
/// so never tag with user ids, emails, category ids or free text.
/// </summary>
public sealed class ForumMetrics
{
    public const string MeterName = "Forum";

    public const string AuthOutcomeSuccess = "success";
    public const string AuthOutcomeInvalidCredentials = "invalid_credentials";
    public const string AuthOutcomeBlocked = "blocked";

    public const string ReactionAdd = "add";
    public const string ReactionRemove = "remove";

    public const string ConsumeOutcomeOk = "ok";
    public const string ConsumeOutcomeRetry = "retry";
    public const string ConsumeOutcomePoison = "poison";
    public const string ConsumeOutcomeDuplicate = "duplicate";

    public const string ErrorCategoryDatabase = "database";
    public const string ErrorCategoryTimeout = "timeout";
    public const string ErrorCategoryOther = "other";

    private readonly Counter<long> _authAttempts;
    private readonly Counter<long> _threadsCreated;
    private readonly Counter<long> _commentsCreated;
    private readonly Counter<long> _reactions;
    private readonly Counter<long> _outboxPublished;
    private readonly Counter<long> _outboxPublishFailures;
    private readonly Histogram<double> _outboxLag;
    private readonly Counter<long> _messagesConsumed;
    private readonly UpDownCounter<long> _wsConnections;
    private readonly UpDownCounter<long> _wsSubscriptions;
    private readonly Counter<long> _wsPushes;
    private readonly Counter<long> _apiRejections;
    private readonly Counter<long> _unhandledErrors;

    /// <summary>Last loop-alive tick per hosted service, surfaced as an age gauge so a silently dead loop is visible.</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _hostedServiceTicks = new();
    private readonly TimeProvider _time;

    public ForumMetrics(IMeterFactory factory, TimeProvider time)
    {
        _time = time;
        var meter = factory.Create(MeterName);

        _authAttempts = meter.CreateCounter<long>("forum.auth.attempts", description: "Login attempts by outcome");
        _threadsCreated = meter.CreateCounter<long>("forum.threads.created");
        _commentsCreated = meter.CreateCounter<long>("forum.comments.created");
        _reactions = meter.CreateCounter<long>("forum.reactions", description: "Reaction toggles by action");
        _outboxPublished = meter.CreateCounter<long>("forum.outbox.published", description: "Relay publishes by module");
        _outboxPublishFailures = meter.CreateCounter<long>("forum.outbox.publish_failures");
        _outboxLag = meter.CreateHistogram<double>(
            "forum.outbox.lag", unit: "s", description: "OccurredOn → broker-confirm latency",
            advice: new InstrumentAdvice<double>
            {
                // The default OTel boundaries are tuned for milliseconds; outbox lag lives between the relay's
                // poll interval (sub-second) and a backlog worth alerting on (minutes).
                HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 300],
            });
        _messagesConsumed = meter.CreateCounter<long>(
            "forum.messaging.consumed", description: "Consumed deliveries by module + outcome (ok|retry|poison|duplicate)");
        _wsConnections = meter.CreateUpDownCounter<long>("forum.ws.connections");
        _wsSubscriptions = meter.CreateUpDownCounter<long>("forum.ws.subscriptions");
        _wsPushes = meter.CreateCounter<long>("forum.ws.pushes");
        _apiRejections = meter.CreateCounter<long>(
            "forum.api.rejections", description: "Expected Result-pattern rejections by status code + error type");
        _unhandledErrors = meter.CreateCounter<long>(
            "forum.errors.unhandled", description: "Unhandled exceptions by closed category (client cancellations excluded)");
        meter.CreateObservableGauge(
            "forum.hosted_service.tick_age", ObserveHostedServiceTickAges, unit: "s",
            description: "Seconds since each background loop last reported alive; a growing value means a dead loop");
    }

    public void AuthAttempt(string outcome) =>
        _authAttempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void ThreadCreated() => _threadsCreated.Add(1);

    public void CommentCreated() => _commentsCreated.Add(1);

    /// <summary>Records an actual reaction state change — idempotent no-ops are deliberately not counted.</summary>
    public void ReactionToggled(string action) =>
        _reactions.Add(1, new KeyValuePair<string, object?>("action", action));

    public void OutboxPublished(string module, TimeSpan lag)
    {
        _outboxPublished.Add(1, new KeyValuePair<string, object?>("module", module));
        _outboxLag.Record(lag.TotalSeconds, new KeyValuePair<string, object?>("module", module));
    }

    public void OutboxPublishFailed(string module) =>
        _outboxPublishFailures.Add(1, new KeyValuePair<string, object?>("module", module));

    public void MessageConsumed(string module, string outcome) =>
        _messagesConsumed.Add(
            1, new KeyValuePair<string, object?>("module", module), new KeyValuePair<string, object?>("outcome", outcome));

    public void WsConnectionOpened() => _wsConnections.Add(1);

    public void WsConnectionClosed() => _wsConnections.Add(-1);

    public void WsSubscriptionsChanged(int delta) => _wsSubscriptions.Add(delta);

    public void WsPushSent() => _wsPushes.Add(1);

    /// <summary>Counts an expected 4xx rejection. <paramref name="errorType"/> is the closed ErrorType enum name.</summary>
    public void ApiRejection(int statusCode, string errorType) =>
        _apiRejections.Add(
            1,
            new KeyValuePair<string, object?>("status", statusCode),
            new KeyValuePair<string, object?>("errorType", errorType));

    /// <summary>Counts an unhandled exception. <paramref name="category"/> must be one of the ErrorCategory consts.</summary>
    public void UnhandledError(string category) =>
        _unhandledErrors.Add(1, new KeyValuePair<string, object?>("category", category));

    /// <summary>Marks a background loop alive. Call once per loop pass; Phase 10c alerts on the age gauge.</summary>
    public void HostedServiceTick(string service) => _hostedServiceTicks[service] = _time.GetUtcNow();

    private IEnumerable<Measurement<double>> ObserveHostedServiceTickAges()
    {
        var now = _time.GetUtcNow();
        foreach (var (service, lastTick) in _hostedServiceTicks)
        {
            yield return new Measurement<double>(
                (now - lastTick).TotalSeconds, new KeyValuePair<string, object?>("service", service));
        }
    }
}
