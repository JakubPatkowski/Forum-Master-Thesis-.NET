namespace Forum.Modules.Social.Domain.Notifications;

/// <summary>
/// The DURABLE source of truth for the notification bell (ADR 0010: the WebSocket push for a notification is
/// identity + routing only — clients re-fetch this row, never trust socket payload content). Message arrivals do
/// NOT create rows (the unread chat badge derives from participants' <c>last_read_on_utc</c>; a row per message
/// would explode the table). Plain entity: system-generated, no audit ceremony beyond <see cref="CreatedOnUtc"/>.
/// </summary>
internal sealed class Notification
{
    // EF materialization.
    private Notification()
    {
    }

    public Notification(Ulid id, Ulid userId, string kind, Ulid? actorId, Ulid? targetId, DateTimeOffset createdOnUtc)
    {
        Id = id;
        UserId = userId;
        Kind = kind;
        ActorId = actorId;
        TargetId = targetId;
        CreatedOnUtc = createdOnUtc;
    }

    public Ulid Id { get; private set; }

    /// <summary>The recipient.</summary>
    public Ulid UserId { get; private set; }

    /// <summary>One of <see cref="NotificationKinds"/>.</summary>
    public string Kind { get; private set; } = default!;

    /// <summary>The user whose action triggered this (requester, inviter, kicker), when there is one.</summary>
    public Ulid? ActorId { get; private set; }

    /// <summary>The related entity the client navigates to (friendship, invite or group id, by kind).</summary>
    public Ulid? TargetId { get; private set; }

    public bool IsRead { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; private set; }

    public void MarkRead() => IsRead = true;
}

/// <summary>The closed kind catalog — the SPA switches on these wire strings.</summary>
internal static class NotificationKinds
{
    /// <summary>Someone sent you a friend request (target = friendship id).</summary>
    public const string FriendRequest = "friend.request";

    /// <summary>Your friend request was accepted (target = friendship id).</summary>
    public const string FriendAccepted = "friend.accepted";

    /// <summary>You were invited to a group (target = invite id).</summary>
    public const string GroupInvite = "group.invite";

    /// <summary>Your group invite was accepted (target = group id).</summary>
    public const string GroupInviteAccepted = "group.invite.accepted";

    /// <summary>You were removed from a group (target = group id).</summary>
    public const string GroupKicked = "group.kicked";
}
