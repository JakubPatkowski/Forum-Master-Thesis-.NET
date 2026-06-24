namespace Forum.SharedKernel.Domain;

/// <summary>Base class for entities: identity-based equality.</summary>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    protected Entity(TId id) => Id = id;

    // Parameterless ctor for EF Core materialization.
    protected Entity() { }

    public TId Id { get; protected init; } = default!;

    public bool Equals(Entity<TId>? other) =>
        other is not null && GetType() == other.GetType() &&
        EqualityComparer<TId>.Default.Equals(Id, other.Id);

    public override bool Equals(object? obj) => obj is Entity<TId> entity && Equals(entity);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) => Equals(left, right);

    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !Equals(left, right);
}
