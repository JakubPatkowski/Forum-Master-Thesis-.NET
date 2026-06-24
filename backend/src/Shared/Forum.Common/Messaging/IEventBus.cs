namespace Forum.Common.Messaging;

/// <summary>A fact published from one module for others to react to (durable via the outbox).</summary>
public interface IIntegrationEvent
{
    Ulid EventId { get; }

    DateTimeOffset OccurredOnUtc { get; }
}

/// <summary>In-process bus for cross-module integration events. Modules never call each other directly.</summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent;
}
