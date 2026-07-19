using Forum.Modules.Social.Domain.Presence;

namespace Forum.Modules.Social.Application.Abstractions;

/// <summary>
/// The presence seam (ADR-0011 shape): heartbeat writes + batch status reads. Today's implementation is a plain
/// <c>user_presence</c> table (status computed from heartbeat age at read time — no background sweep); the
/// already-scoped Redis session swaps in a Redis-backed store behind this exact interface with zero caller
/// changes (the §10d ticket-cache "config-gated strategy" idiom).
/// </summary>
internal interface IPresenceStore
{
    Task HeartbeatAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>
    /// One round-trip batch (Engagement's reaction-batch precedent). Users with <c>ShowOnlineStatus = false</c>
    /// read as <see cref="PresenceStatus.Offline"/>; unknown ids zero-fill to offline too.
    /// </summary>
    Task<IReadOnlyDictionary<Ulid, PresenceStatus>> GetStatusesAsync(
        IReadOnlyList<Ulid> userIds, CancellationToken cancellationToken);
}
