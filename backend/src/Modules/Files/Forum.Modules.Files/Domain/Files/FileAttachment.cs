namespace Forum.Modules.Files.Domain.Files;

/// <summary>
/// File ↔ target link row (composite key, owned by the <see cref="StoredFile"/> aggregate). The target id is a
/// logical ULID reference into another module's schema — never a DB foreign key.
/// </summary>
internal sealed class FileAttachment
{
    // EF materialization.
    private FileAttachment()
    {
    }

    public FileAttachment(Ulid fileId, FileTargetType targetType, Ulid targetId, DateTimeOffset createdOnUtc)
    {
        FileId = fileId;
        TargetType = targetType;
        TargetId = targetId;
        CreatedOnUtc = createdOnUtc;
    }

    public Ulid FileId { get; private set; }

    public FileTargetType TargetType { get; private set; }

    public Ulid TargetId { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; private set; }
}
