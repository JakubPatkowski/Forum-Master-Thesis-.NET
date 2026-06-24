namespace Forum.SharedKernel.Domain;

/// <summary>Marks an entity whose audit columns are stamped automatically by the persistence interceptor.</summary>
public interface IAuditableEntity
{
    DateTimeOffset CreatedOnUtc { get; }
    DateTimeOffset? LastModifiedOnUtc { get; }
    Ulid? CreatedBy { get; }
    Ulid? LastModifiedBy { get; }

    void SetCreated(DateTimeOffset onUtc, Ulid? by);
    void SetModified(DateTimeOffset onUtc, Ulid? by);
}
