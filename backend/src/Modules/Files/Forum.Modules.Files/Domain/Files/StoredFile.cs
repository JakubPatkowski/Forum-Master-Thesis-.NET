using System.Globalization;

using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Files.Domain.Files;

/// <summary>
/// One uploaded object in the bucket (ADR 0008). Born <see cref="FileStatus.Pending"/> when a presigned PUT is
/// issued; flips to <see cref="FileStatus.Committed"/> only after the stored object's REAL size and sniffed
/// content type were verified against the declared values. Owned by the uploader (<see cref="OwnerId"/> maps to
/// the <c>uploaded_by</c> column). Orphans are physically deleted by the sweep — no soft-delete: blobs are
/// storage, not content.
/// </summary>
internal sealed class StoredFile : AggregateRoot<Ulid>, IOwned
{
    private readonly List<FileAttachment> _attachments = [];

    // EF materialization.
    private StoredFile()
    {
    }

    private StoredFile(Ulid id, string bucket, string objectKey, string contentType, long sizeBytes, Ulid ownerId)
        : base(id)
    {
        Bucket = bucket;
        ObjectKey = objectKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Status = FileStatus.Pending;
        OwnerId = ownerId;
    }

    public string Bucket { get; private set; } = default!;

    /// <summary>Bucket key, derived from the ULID (unique, month-sharded, human-browsable).</summary>
    public string ObjectKey { get; private set; } = default!;

    /// <summary>Declared at initiate; re-verified against the sniffed type at commit.</summary>
    public string ContentType { get; private set; } = default!;

    /// <summary>Declared at initiate; re-verified against the stored object's real size at commit.</summary>
    public long SizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public FileStatus Status { get; private set; }

    /// <summary>The user who requested the presigned URL (column <c>uploaded_by</c>).</summary>
    public Ulid OwnerId { get; private set; }

    public DateTimeOffset? CommittedOnUtc { get; private set; }

    public IReadOnlyCollection<FileAttachment> Attachments => _attachments.AsReadOnly();

    public bool IsAttached => _attachments.Count > 0;

    /// <summary>Creates a pending file row for a presigned upload. Declared values are validated by the handler.</summary>
    public static StoredFile Create(string bucket, string contentType, long sizeBytes, Ulid ownerId)
    {
        var id = Ulid.NewUlid();
        // ULID-derived key: unique by construction, sharded by upload month so the bucket stays browsable.
        // Invariant culture keeps the '/' literal (a culture-specific date separator would corrupt the key).
        var objectKey = $"{id.Time.ToString("yyyy/MM", CultureInfo.InvariantCulture)}/{id}";
        return new StoredFile(id, bucket, objectKey, contentType.ToLowerInvariant(), sizeBytes, ownerId);
    }

    /// <summary>
    /// Verifies the stored object against the declared values and flips the row to committed. The inputs come
    /// from the store itself (stat) and from sniffing the object's magic bytes — never from the client.
    /// </summary>
    public Result Commit(long actualSizeBytes, string sniffedContentType, int width, int height, DateTimeOffset onUtc)
    {
        if (Status == FileStatus.Committed)
        {
            return Result.Failure(FileErrors.AlreadyCommitted);
        }

        if (actualSizeBytes != SizeBytes)
        {
            return Result.Failure(FileErrors.SizeMismatch);
        }

        if (!string.Equals(sniffedContentType, ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(FileErrors.TypeMismatch);
        }

        Width = width;
        Height = height;
        Status = FileStatus.Committed;
        CommittedOnUtc = onUtc;
        return Result.Success();
    }

    /// <summary>Links the file to a target. Idempotent: attaching an existing link is a no-op success.</summary>
    public Result Attach(FileTargetType targetType, Ulid targetId, DateTimeOffset onUtc)
    {
        if (Status != FileStatus.Committed)
        {
            return Result.Failure(FileErrors.NotCommitted);
        }

        if (!IsAttachedTo(targetType, targetId))
        {
            _attachments.Add(new FileAttachment(Id, targetType, targetId, onUtc));
        }

        return Result.Success();
    }

    /// <summary>Removes the link to a target. Idempotent: detaching a missing link is a no-op success.</summary>
    public Result Detach(FileTargetType targetType, Ulid targetId)
    {
        _attachments.RemoveAll(attachment =>
            attachment.TargetType == targetType && attachment.TargetId == targetId);
        return Result.Success();
    }

    public bool IsAttachedTo(FileTargetType targetType, Ulid targetId) =>
        _attachments.Exists(attachment =>
            attachment.TargetType == targetType && attachment.TargetId == targetId);
}
