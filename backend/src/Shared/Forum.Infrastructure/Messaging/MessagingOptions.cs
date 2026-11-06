namespace Forum.Infrastructure.Messaging;

/// <summary>Tuning for the outbox relays and consumer hosts, bound from the "Messaging" configuration section.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>
    /// How often each module's relay polls its outbox for unpublished rows. With <see cref="WakeRelayOnWrite"/> this
    /// is the fallback cadence (other replicas' rows, crash recovery); the same replica's writes wake the relay at once.
    /// </summary>
    public int PollIntervalMilliseconds { get; init; } = 5_000;

    /// <summary>
    /// Signal the module's relay on this replica as soon as a transaction that inserted outbox rows has committed
    /// (A/B v5, A4) instead of letting the rows wait for the next poll. Off = pure polling.
    /// </summary>
    public bool WakeRelayOnWrite { get; init; } = true;

    /// <summary>Outbox rows claimed per relay pass (the FOR UPDATE SKIP LOCKED window).</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Ceiling for the relay's exponential backoff after a failed publish pass.</summary>
    public int MaxPublishBackoffMilliseconds { get; init; } = 30_000;

    /// <summary>How long a failed delivery waits in the retry queue before re-entering the work queue (its <c>x-message-ttl</c>).</summary>
    public int RetryDelayMilliseconds { get; init; } = 5_000;

    /// <summary>Total deliveries (first + redeliveries) before a persistently failing message is parked in the poison queue.</summary>
    public int MaxDeliveryAttempts { get; init; } = 5;

    /// <summary>Unacknowledged messages prefetched per consumer channel.</summary>
    public ushort Prefetch { get; init; } = 10;
}
