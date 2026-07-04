namespace Forum.Modules.Engagement.Domain.Reactions;

/// <summary>
/// One user's reaction of one kind on one target (composite key — no surrogate id). The target is a logical
/// ULID reference into <c>forum_content</c>, never a DB foreign key; deletion-event consumers keep it consistent.
/// <see cref="ReactionType"/> and <see cref="Value"/> are two independent axes: the kind ('like' today, more
/// later) and the signed weight (+1 now, extensible to -1 downvotes).
/// </summary>
internal sealed class Reaction
{
    // EF materialization.
    private Reaction()
    {
    }

    public Reaction(Ulid userId, ReactionTargetType targetType, Ulid targetId, string reactionType, DateTimeOffset createdOnUtc)
    {
        UserId = userId;
        TargetType = targetType;
        TargetId = targetId;
        ReactionType = reactionType;
        Value = 1;
        CreatedOnUtc = createdOnUtc;
    }

    public Ulid UserId { get; private set; }

    public ReactionTargetType TargetType { get; private set; }

    public Ulid TargetId { get; private set; }

    public string ReactionType { get; private set; } = default!;

    public short Value { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; private set; }
}
