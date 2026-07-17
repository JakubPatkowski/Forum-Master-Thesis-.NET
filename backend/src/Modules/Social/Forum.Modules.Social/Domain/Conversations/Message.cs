using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Domain.Conversations;

/// <summary>
/// One chat message (markdown, stored raw). Delete is a tombstone exactly like Content's Comment: the row stays,
/// the body becomes <see cref="DeletedBody"/> and the history view keeps returning it — a hard delete would make
/// conversations silently rewrite themselves. Edits stamp <see cref="EditedOnUtc"/> so clients can show an
/// "edited" marker without exposing audit columns.
/// </summary>
internal sealed class Message : AggregateRoot<Ulid>, IOwned, ISoftDeletable
{
    public const int MaxBodyLength = 4000;

    /// <summary>What readers see instead of a soft-deleted message's body.</summary>
    public const string DeletedBody = "[deleted]";

    // EF materialization.
    private Message()
    {
    }

    private Message(Ulid id, Ulid conversationId, Ulid senderId, string body) : base(id)
    {
        ConversationId = conversationId;
        OwnerId = senderId;
        Body = body;
    }

    public Ulid ConversationId { get; private set; }

    /// <summary>The sender (named OwnerId to carry the shared <see cref="IOwned"/> ownership semantics).</summary>
    public Ulid OwnerId { get; private set; }

    public string Body { get; private set; } = default!;

    public DateTimeOffset? EditedOnUtc { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedOnUtc { get; private set; }

    public Ulid? DeletedBy { get; private set; }

    public static Message Create(Ulid conversationId, Ulid senderId, string body) =>
        new(Ulid.NewUlid(), conversationId, senderId, body);

    public Result Edit(string body, DateTimeOffset onUtc)
    {
        if (IsDeleted)
        {
            return Result.Failure(MessageErrors.Deleted);
        }

        Body = body;
        EditedOnUtc = onUtc;
        return Result.Success();
    }

    /// <summary>Soft-deletes: flags the row and blanks the body; history keeps the tombstone.</summary>
    public Result Delete(Ulid deletedBy, DateTimeOffset onUtc)
    {
        if (IsDeleted)
        {
            return Result.Failure(MessageErrors.Deleted);
        }

        MarkDeleted(onUtc, deletedBy);
        Body = DeletedBody;
        return Result.Success();
    }

    public void MarkDeleted(DateTimeOffset onUtc, Ulid? by)
    {
        IsDeleted = true;
        DeletedOnUtc = onUtc;
        DeletedBy = by;
    }

    /// <summary>Offline-seeder constructor: deterministic id + audit, no events.</summary>
    internal static Message Seed(Ulid id, Ulid conversationId, Ulid senderId, string body, DateTimeOffset createdOnUtc)
    {
        var message = new Message(id, conversationId, senderId, body);
        message.SetCreated(createdOnUtc, senderId);
        return message;
    }
}
