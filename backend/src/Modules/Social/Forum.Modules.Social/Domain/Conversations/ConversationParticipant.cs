namespace Forum.Modules.Social.Domain.Conversations;

/// <summary>
/// One user's seat in a conversation — THE single authorization fact every message read/send checks, for DMs and
/// group chats alike (group membership changes write through here in the same transaction). A left participant
/// keeps the row with <see cref="LeftOnUtc"/> set (re-joining clears it), so history stays attributable.
/// <see cref="LastReadOnUtc"/> feeds the OWNER's own unread badge only — read receipts to the sender are
/// deliberately out of scope (REQUIREMENTS-AND-ASSUMPTIONS §1) and must not be built.
/// </summary>
internal sealed class ConversationParticipant
{
    // EF materialization.
    private ConversationParticipant()
    {
    }

    public ConversationParticipant(Ulid conversationId, Ulid userId, DateTimeOffset joinedOnUtc)
    {
        ConversationId = conversationId;
        UserId = userId;
        JoinedOnUtc = joinedOnUtc;
    }

    public Ulid ConversationId { get; private set; }

    public Ulid UserId { get; private set; }

    public DateTimeOffset JoinedOnUtc { get; private set; }

    public DateTimeOffset? LeftOnUtc { get; private set; }

    public DateTimeOffset? LastReadOnUtc { get; private set; }

    public bool IsMuted { get; private set; }

    /// <summary>An active participant may read and send; a left one may do neither.</summary>
    public bool IsActive => LeftOnUtc is null;

    public void Leave(DateTimeOffset onUtc) => LeftOnUtc = onUtc;

    public void Rejoin(DateTimeOffset onUtc)
    {
        LeftOnUtc = null;
        JoinedOnUtc = onUtc;
    }

    public void MarkRead(DateTimeOffset onUtc) => LastReadOnUtc = onUtc;

    public void SetMuted(bool muted) => IsMuted = muted;
}
