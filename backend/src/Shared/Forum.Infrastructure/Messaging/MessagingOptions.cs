namespace Forum.Infrastructure.Messaging;

/// <summary>Tuning for the outbox relays and consumer hosts, bound from the "Messaging" configuration section.</summary>
public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";

    /// <summary>How often each module's relay polls its outbox for unpublished rows.</summary>
    public int PollIntervalMilliseconds { get; init; } = 5_000;

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
