using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Domain.Groups;

/// <summary>
/// A user-owned social group. Visibility governs discovery and joining only (Public = listed + joinable without an
/// invite, Private = invite-only); the group's chat and member list are member-only in BOTH cases. Role split:
/// membership is this module's own fact (<see cref="GroupMembership"/>), what a member may DO beyond posting
/// (rename, invite, kick) is the ACL's <c>moderate</c> bit at <c>group</c> scope — mirroring category
/// ownership/visibility (Content-owned) vs. moderate permission (ACL-owned). No icon column: group icons ride
/// Files' attachments-by-target read, exactly like avatars.
/// </summary>
internal sealed class Group : AggregateRoot<Ulid>, IOwned, ISoftDeletable
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 2000;

    // EF materialization.
    private Group()
    {
    }

    private Group(Ulid id, string name, string description, GroupVisibility visibility, Ulid ownerId) : base(id)
    {
        Name = name;
        Description = description;
        Visibility = visibility;
        OwnerId = ownerId;
    }

    public string Name { get; private set; } = default!;

    public string Description { get; private set; } = default!;

    public GroupVisibility Visibility { get; private set; }

    public Ulid OwnerId { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedOnUtc { get; private set; }

    public Ulid? DeletedBy { get; private set; }

    public static Group Create(string name, string description, GroupVisibility visibility, Ulid ownerId) =>
        new(Ulid.NewUlid(), name.Trim(), description.Trim(), visibility, ownerId);

    public void Update(string name, string description, GroupVisibility visibility)
    {
        Name = name.Trim();
        Description = description.Trim();
        Visibility = visibility;
    }

    /// <summary>Ownership transfer target must already be a member — the handler enforces that fact.</summary>
    public void TransferOwnership(Ulid newOwnerId) => OwnerId = newOwnerId;

    public Result Delete(Ulid deletedBy, DateTimeOffset onUtc)
    {
        if (IsDeleted)
        {
            return Result.Failure(GroupErrors.AlreadyDeleted);
        }

        MarkDeleted(onUtc, deletedBy);
        return Result.Success();
    }

    public void MarkDeleted(DateTimeOffset onUtc, Ulid? by)
    {
        IsDeleted = true;
        DeletedOnUtc = onUtc;
        DeletedBy = by;
    }

    /// <summary>Offline-seeder constructor: deterministic id + audit, no events.</summary>
    internal static Group Seed(
        Ulid id, string name, string description, GroupVisibility visibility, Ulid ownerId, DateTimeOffset createdOnUtc)
    {
        var group = new Group(id, name, description, visibility, ownerId);
        group.SetCreated(createdOnUtc, ownerId);
        return group;
    }
}

/// <summary>Stored as text (Content's Visibility precedent), never as a PG enum.</summary>
internal enum GroupVisibility
{
    Public,
    Private,
}
