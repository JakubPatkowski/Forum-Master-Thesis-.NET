using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Files.Infrastructure.Persistence;

internal sealed class StoredFileRepository : IStoredFileRepository
{
    private readonly FilesDbContext _db;

    public StoredFileRepository(FilesDbContext db) => _db = db;

    public Task<StoredFile?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Files.AsTracking()
            .Include(file => file.Attachments)
            .FirstOrDefaultAsync(file => file.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StoredFile>> GetAttachedToTargetAsync(
        FileTargetType targetType, Ulid targetId, CancellationToken cancellationToken) =>
        await _db.Files.AsTracking()
            .Include(file => file.Attachments)
            .Where(file => file.Attachments.Any(
                attachment => attachment.TargetType == targetType && attachment.TargetId == targetId))
            .ToListAsync(cancellationToken);

    public Task<int> CountAttachmentsForTargetAsync(
        FileTargetType targetType, Ulid targetId, CancellationToken cancellationToken) =>
        _db.FileAttachments.CountAsync(
            attachment => attachment.TargetType == targetType && attachment.TargetId == targetId,
            cancellationToken);

    public async Task<IReadOnlyList<StoredFile>> GetExpiredPendingAsync(
        DateTimeOffset cutoffUtc, int limit, CancellationToken cancellationToken) =>
        await _db.Files.AsTracking()
            .Where(file => file.Status == FileStatus.Pending && file.CreatedOnUtc < cutoffUtc)
            .OrderBy(file => file.CreatedOnUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StoredFile>> GetUnattachedCommittedAsync(
        DateTimeOffset cutoffUtc, int limit, CancellationToken cancellationToken) =>
        await _db.Files.AsTracking()
            .Where(file => file.Status == FileStatus.Committed
                && file.CommittedOnUtc < cutoffUtc
                && !file.Attachments.Any())
            .OrderBy(file => file.CommittedOnUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(StoredFile file) => _db.Files.Add(file);

    public void Remove(StoredFile file) => _db.Files.Remove(file);
}
