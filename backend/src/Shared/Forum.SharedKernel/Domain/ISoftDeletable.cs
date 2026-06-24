namespace Forum.SharedKernel.Domain;

/// <summary>Marks an aggregate that is removed by flagging rather than physical delete. A global query filter hides flagged rows.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedOnUtc { get; }

    Ulid? DeletedBy { get; }

    void MarkDeleted(DateTimeOffset onUtc, Ulid? by);
}
