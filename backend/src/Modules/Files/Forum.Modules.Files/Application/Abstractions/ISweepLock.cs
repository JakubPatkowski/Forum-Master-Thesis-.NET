namespace Forum.Modules.Files.Application.Abstractions;

/// <summary>
/// Cross-replica mutual exclusion for the orphan sweep, so scaled-out pods don't all sweep at once
/// (a Postgres advisory lock in production). Deletions are idempotent either way; the lock only avoids
/// duplicate work, not corruption.
/// </summary>
internal interface ISweepLock
{
    /// <summary>True when this instance acquired the sweep lock; false means another replica is sweeping.</summary>
    Task<bool> TryAcquireAsync(CancellationToken cancellationToken);

    Task ReleaseAsync(CancellationToken cancellationToken);
}
