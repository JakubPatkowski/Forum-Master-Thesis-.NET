using Forum.Modules.Content.Domain.Comments.Events;
using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Domain.Comments;

/// <summary>
/// A comment in a thread, nested via materialized path: <c>path</c> is the dot-joined chain of ULIDs from the root
/// down to this comment, so <c>ORDER BY path</c> reads the tree depth-first with a deterministic ULID tiebreak.
/// Soft-delete blanks the body to a placeholder and keeps the subtree attached.
/// </summary>
internal sealed class Comment : AggregateRoot<Ulid>, IOwned, ISoftDeletable
{
    /// <summary>Maximum nesting depth; the root sits at depth 0.</summary>
    public const int MaxDepth = 5;

    /// <summary>What readers see instead of a soft-deleted comment's body.</summary>
    public const string DeletedBody = "[deleted]";

    // EF materialization.
    private Comment()
    {
    }

    private Comment(Ulid id, Ulid threadId, Ulid? parentId, Ulid ownerId, string body, string path, int depth)
        : base(id)
    {
        ThreadId = threadId;
        ParentId = parentId;
        OwnerId = ownerId;
        Body = body;
        Path = path;
        Depth = depth;
    }

    public Ulid ThreadId { get; private set; }

    public Ulid? ParentId { get; private set; }

    public Ulid OwnerId { get; private set; }

    /// <summary>Markdown, stored raw; <see cref="DeletedBody"/> once soft-deleted.</summary>
    public string Body { get; private set; } = default!;

    /// <summary>Materialized path: root ULID for roots, <c>parent.Path + "." + own ULID</c> for replies.</summary>
    public string Path { get; private set; } = default!;

    public int Depth { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedOnUtc { get; private set; }

    public Ulid? DeletedBy { get; private set; }

    /// <summary>Creates a top-level comment: path is its own ULID, depth 0.</summary>
    public static Comment CreateRoot(Ulid threadId, Ulid ownerId, string body)
    {
        var id = Ulid.NewUlid();
        var comment = new Comment(id, threadId, parentId: null, ownerId, body, path: id.ToString(), depth: 0);
        comment.Raise(new CommentCreatedDomainEvent(id, threadId, null, ownerId, DateTimeOffset.UtcNow));
        return comment;
    }

    /// <summary>Creates a reply under <paramref name="parent"/>, extending its path and enforcing the depth cap.</summary>
    public static Result<Comment> CreateReply(Comment parent, Ulid ownerId, string body)
    {
        if (parent.Depth >= MaxDepth)
        {
            return Result.Failure<Comment>(CommentErrors.MaxDepthExceeded);
        }

        var id = Ulid.NewUlid();
        var comment = new Comment(
            id, parent.ThreadId, parent.Id, ownerId, body, path: $"{parent.Path}.{id}", depth: parent.Depth + 1);
        comment.Raise(new CommentCreatedDomainEvent(id, parent.ThreadId, parent.Id, ownerId, DateTimeOffset.UtcNow));
        return Result.Success(comment);
    }

    public void Update(string body) => Body = body;

    /// <summary>Soft-deletes: flags the row, blanks the body, keeps children attached to the tree.</summary>
    public Result Delete(Ulid deletedBy, DateTimeOffset onUtc)
    {
        if (IsDeleted)
        {
            return Result.Failure(CommentErrors.AlreadyDeleted);
        }

        MarkDeleted(onUtc, deletedBy);
        Body = DeletedBody;
        Raise(new CommentDeletedDomainEvent(Id, ThreadId, deletedBy, onUtc));
        return Result.Success();
    }

    public void MarkDeleted(DateTimeOffset onUtc, Ulid? by)
    {
        IsDeleted = true;
        DeletedOnUtc = onUtc;
        DeletedBy = by;
    }
}
