namespace Forum.Modules.Social.Domain.Presence;

/// <summary>
/// Ephemeral heartbeat state — deliberately NOT an audited aggregate (high-write-frequency, zero business value in
/// history). Status is COMPUTED at read time from the heartbeat's age (no background sweep flips rows): online
/// under the online threshold, away under the away threshold, offline beyond it or when no row exists. Lives
/// behind <see cref="Application.Abstractions.IPresenceStore"/> so the already-scoped Redis session can swap the
/// store without touching a single call site.
/// </summary>
internal sealed class UserPresence
{
    // EF materialization.
    private UserPresence()
    {
    }

    public UserPresence(Ulid userId, DateTimeOffset lastHeartbeatOnUtc)
    {
        UserId = userId;
        LastHeartbeatOnUtc = lastHeartbeatOnUtc;
    }

    public Ulid UserId { get; private set; }

    public DateTimeOffset LastHeartbeatOnUtc { get; private set; }
}

/// <summary>Wire values are the lowercase names.</summary>
internal enum PresenceStatus
{
    Offline,
    Away,
    Online,
}
