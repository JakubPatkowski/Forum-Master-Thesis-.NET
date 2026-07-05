namespace Forum.Infrastructure.Messaging.Inbox;

/// <summary>
/// A processed-event marker giving consumers structural idempotency under at-least-once delivery. Each consuming
/// module owns its own <c>inbox_messages</c> table; the consumer host inserts the row in the same transaction as
/// the handlers' effects, so a redelivered event hits the primary key and is skipped instead of re-applied.
/// </summary>
public sealed class InboxMessage
{
    /// <summary>The integration event's <c>EventId</c> — identical across redeliveries, unique across events.</summary>
    public Ulid Id { get; init; }

    public DateTimeOffset ProcessedOnUtc { get; init; }
}
