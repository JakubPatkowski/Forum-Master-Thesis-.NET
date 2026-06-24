namespace Forum.Infrastructure.Messaging.Outbox;

/// <summary>
/// A transactional-outbox row. Each module owns its own outbox table (in the module's schema), written in the
/// same transaction as the state change; the relay (Phase 6) publishes it to RabbitMQ and stamps <see cref="ProcessedOnUtc"/>.
/// </summary>
public sealed class OutboxMessage
{
    public Ulid Id { get; init; }

    /// <summary>Assembly-qualified or contract type name of the integration event.</summary>
    public required string Type { get; init; }

    /// <summary>Serialized event payload (JSON).</summary>
    public required string Payload { get; init; }

    public DateTimeOffset OccurredOnUtc { get; init; }

    public DateTimeOffset? ProcessedOnUtc { get; set; }

    public string? Error { get; set; }
}
