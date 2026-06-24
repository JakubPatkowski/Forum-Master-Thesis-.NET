namespace Forum.Common.Messaging;

/// <summary>Reacts to an integration event published by another module. Resolved in-process now, via the RabbitMQ relay from Phase 6.</summary>
public interface IIntegrationEventHandler<in TEvent>
    where TEvent : class, IIntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
