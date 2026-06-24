using Forum.SharedKernel.Domain;

namespace Forum.Infrastructure.Messaging;

/// <summary>Dispatches domain events to their in-process handlers after the unit of work has committed.</summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
