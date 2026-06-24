namespace Forum.SharedKernel.Domain;

/// <summary>Marks an aggregate that has an owner (author). Ownership gates author-vs-`*.any` authorization.</summary>
public interface IOwned
{
    Ulid OwnerId { get; }
}
