namespace Forum.SharedKernel.Domain;

/// <summary>Non-generic view of an aggregate's raised domain events, so the persistence layer can collect them regardless of the id type.</summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}
