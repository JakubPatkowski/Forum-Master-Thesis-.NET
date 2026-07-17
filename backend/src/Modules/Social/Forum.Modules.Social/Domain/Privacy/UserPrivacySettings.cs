namespace Forum.Modules.Social.Domain.Privacy;

/// <summary>
/// Pure preference state (no audit ceremony, no events — deliberately NOT an aggregate): who may friend-request,
/// DM or group-invite this user, and whether their presence is visible. A missing row means the defaults; the row
/// is created lazily on first change. Enforced inside the relevant send/invite handlers in 404→403→422 order.
/// </summary>
internal sealed class UserPrivacySettings
{
    // EF materialization.
    private UserPrivacySettings()
    {
    }

    public UserPrivacySettings(Ulid userId)
    {
        UserId = userId;
        FriendRequests = PrivacyAudience.Everyone;
        Messages = PrivacyAudience.Everyone;
        GroupInvites = PrivacyAudience.Everyone;
        ShowOnlineStatus = true;
    }

    public Ulid UserId { get; private set; }

    /// <summary><see cref="PrivacyAudience.Friends"/> is meaningless for friend requests and is normalized to
    /// <see cref="PrivacyAudience.NoOne"/> by the update handler.</summary>
    public PrivacyAudience FriendRequests { get; private set; }

    public PrivacyAudience Messages { get; private set; }

    public PrivacyAudience GroupInvites { get; private set; }

    /// <summary>False = everyone else reads this user's presence as offline.</summary>
    public bool ShowOnlineStatus { get; private set; }

    public void Update(
        PrivacyAudience friendRequests, PrivacyAudience messages, PrivacyAudience groupInvites, bool showOnlineStatus)
    {
        FriendRequests = friendRequests == PrivacyAudience.Friends ? PrivacyAudience.NoOne : friendRequests;
        Messages = messages;
        GroupInvites = groupInvites;
        ShowOnlineStatus = showOnlineStatus;
    }
}

/// <summary>Stored as text, never as a PG enum.</summary>
internal enum PrivacyAudience
{
    Everyone,
    Friends,
    NoOne,
}
