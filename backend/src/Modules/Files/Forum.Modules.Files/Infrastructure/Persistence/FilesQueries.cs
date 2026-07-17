using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Files.Infrastructure.Persistence;

/// <summary>The Files read side: single-module, bounded lookups over no-tracking EF (the context default).</summary>
internal sealed class FilesQueries : IFilesQueries
{
    private readonly FilesDbContext _db;

    public FilesQueries(FilesDbContext db) => _db = db;

    public async Task<FileReadModel?> GetCommittedAsync(Ulid fileId, CancellationToken cancellationToken)
    {
        var file = await _db.Files
            .FirstOrDefaultAsync(
                file => file.Id == fileId && file.Status == FileStatus.Committed, cancellationToken);
        return file is null ? null : ToReadModel(file);
    }

    public async Task<IReadOnlyList<FileReadModel>> ListCommittedForTargetAsync(
        FileTargetType targetType, Ulid targetId, CancellationToken cancellationToken)
    {
        var files = await _db.Files
            .Where(file => file.Status == FileStatus.Committed && file.Attachments.Any(
                attachment => attachment.TargetType == targetType && attachment.TargetId == targetId))
            .OrderBy(file => file.Id)
            .ToListAsync(cancellationToken);
        return files.Select(ToReadModel).ToArray();
    }

    public async Task<IReadOnlyList<AttachmentRef>> GetAttachmentRefsAsync(
        Ulid fileId, CancellationToken cancellationToken) =>
        await _db.Files
            .Where(file => file.Id == fileId)
            .SelectMany(file => file.Attachments)
            .Select(attachment => new AttachmentRef(attachment.TargetType, attachment.TargetId))
            .ToListAsync(cancellationToken);

    private static FileReadModel ToReadModel(StoredFile file) =>
        new(file.Id, file.ObjectKey, file.ContentType, file.SizeBytes, file.Width, file.Height);
}
