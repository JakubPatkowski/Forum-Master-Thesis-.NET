using Forum.SharedKernel.Domain;

namespace Forum.Modules.Social.Domain.Groups;

/// <summary>
/// A pending invitation into a group. Accept turns it into a membership and deletes the row; decline and cancel
/// just delete it (no tombstones — same stance as friend requests). Uniqueness: one pending invite per
/// (group, invitee), enforced by a unique index.
/// </summary>
internal sealed class GroupInvite : AggregateRoot<Ulid>
{
    // EF materialization.
    private GroupInvite()
    {
    }

    private GroupInvite(Ulid id, Ulid groupId, Ulid invitedUserId, Ulid invitedBy) : base(id)
    {
        GroupId = groupId;
        InvitedUserId = invitedUserId;
        InvitedBy = invitedBy;
    }

    public Ulid GroupId { get; private set; }

    public Ulid InvitedUserId { get; private set; }

    public Ulid InvitedBy { get; private set; }

    public static GroupInvite Create(Ulid groupId, Ulid invitedUserId, Ulid invitedBy) =>
        new(Ulid.NewUlid(), groupId, invitedUserId, invitedBy);

    /// <summary>Offline-seeder constructor: deterministic id + audit, no events.</summary>
    internal static GroupInvite Seed(
        Ulid id, Ulid groupId, Ulid invitedUserId, Ulid invitedBy, DateTimeOffset createdOnUtc)
    {
        var invite = new GroupInvite(id, groupId, invitedUserId, invitedBy);
        invite.SetCreated(createdOnUtc, invitedBy);
        return invite;
    }
}
