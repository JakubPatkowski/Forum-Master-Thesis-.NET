namespace Forum.Modules.Social.Domain.Groups;

/// <summary>
/// THE membership fact: this row existing is what "user is in the group" means everywhere (composite key, Reaction
/// precedent). The owner has a row too. Admin-level rights live in the ACL (<c>moderate</c>@group), never here.
/// Membership changes write through to the group conversation's participants in the same transaction, so message
/// authorization stays a single participant check.
/// </summary>
internal sealed class GroupMembership
{
    // EF materialization.
    private GroupMembership()
    {
    }

    public GroupMembership(Ulid groupId, Ulid userId, DateTimeOffset joinedOnUtc, Ulid? invitedBy)
    {
        GroupId = groupId;
        UserId = userId;
        JoinedOnUtc = joinedOnUtc;
        InvitedBy = invitedBy;
    }

    public Ulid GroupId { get; private set; }

    public Ulid UserId { get; private set; }

    public DateTimeOffset JoinedOnUtc { get; private set; }

    public Ulid? InvitedBy { get; private set; }
}
