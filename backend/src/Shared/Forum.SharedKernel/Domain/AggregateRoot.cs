namespace Forum.SharedKernel.Domain;

/// <summary>Aggregate root: the only entry point for mutating an aggregate. Records domain events and carries audit columns.</summary>
public abstract class AggregateRoot<TId> : Entity<TId>, IAuditableEntity, IHasDomainEvents
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot(TId id) : base(id) { }

    protected AggregateRoot() { }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public DateTimeOffset CreatedOnUtc { get; private set; }

    public DateTimeOffset? LastModifiedOnUtc { get; private set; }

    public Ulid? CreatedBy { get; private set; }

    public Ulid? LastModifiedBy { get; private set; }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();

    // Called by the audit interceptor on insert/update; never set by hand in domain code.
    public void SetCreated(DateTimeOffset onUtc, Ulid? by)
    {
        CreatedOnUtc = onUtc;
        CreatedBy = by;
    }

    public void SetModified(DateTimeOffset onUtc, Ulid? by)
    {
        LastModifiedOnUtc = onUtc;
        LastModifiedBy = by;
    }
}
