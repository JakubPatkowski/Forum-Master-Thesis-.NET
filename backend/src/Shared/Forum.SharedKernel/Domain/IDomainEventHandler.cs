namespace Forum.SharedKernel.Domain;

/// <summary>Handles a domain event after the unit of work commits.</summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);
}
