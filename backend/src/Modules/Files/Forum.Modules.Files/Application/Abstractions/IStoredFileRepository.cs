using Forum.Modules.Files.Domain.Files;

namespace Forum.Modules.Files.Application.Abstractions;

/// <summary>Write-side port for the <see cref="StoredFile"/> aggregate (tracked loads include attachments).</summary>
internal interface IStoredFileRepository
{
    Task<StoredFile?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    /// <summary>Files currently linked to the given target (used by deletion-event consumers and replace semantics).</summary>
    Task<IReadOnlyList<StoredFile>> GetAttachedToTargetAsync(
        FileTargetType targetType, Ulid targetId, CancellationToken cancellationToken);

    /// <summary>How many files are linked to the target (enforces the per-target attachment cap).</summary>
    Task<int> CountAttachmentsForTargetAsync(FileTargetType targetType, Ulid targetId, CancellationToken cancellationToken);

    /// <summary>Pending files whose grace window expired (never committed) — orphan-sweep candidates.</summary>
    Task<IReadOnlyList<StoredFile>> GetExpiredPendingAsync(
        DateTimeOffset cutoffUtc, int limit, CancellationToken cancellationToken);

    /// <summary>Committed files with no attachment left after the grace window — orphan-sweep candidates.</summary>
    Task<IReadOnlyList<StoredFile>> GetUnattachedCommittedAsync(
        DateTimeOffset cutoffUtc, int limit, CancellationToken cancellationToken);

    void Add(StoredFile file);

    /// <summary>Physically removes the row (attachments cascade). Only the orphan sweep deletes files.</summary>
    void Remove(StoredFile file);
}
