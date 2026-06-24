namespace Forum.SharedKernel.Domain;

/// <summary>A fact that happened inside an aggregate. Raised in the domain, dispatched after commit.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredOnUtc { get; }
}
