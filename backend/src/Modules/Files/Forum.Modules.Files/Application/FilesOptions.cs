namespace Forum.Modules.Files.Application;

/// <summary>Upload policy + sweep tuning for the Files module, bound from the "Files" configuration section.</summary>
internal sealed class FilesOptions
{
    public const string SectionName = "Files";

    /// <summary>How long the presigned PUT returned by initiate stays valid.</summary>
    public int UploadUrlTtlMinutes { get; init; } = 10;

    /// <summary>How long presigned GET download URLs stay valid.</summary>
    public int DownloadUrlTtlMinutes { get; init; } = 15;

    /// <summary>Maximum declared (and real) object size. Default 5 MiB.</summary>
    public long MaxSizeBytes { get; init; } = 5 * 1024 * 1024;

    /// <summary>Content types accepted at initiate; commit re-verifies the REAL type against this list.</summary>
    public string[] AllowedContentTypes { get; init; } = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    /// <summary>
    /// How many leading bytes of the stored object are fetched at commit to sniff the magic bytes and decode
    /// dimensions. 128 KiB accommodates JPEGs with large EXIF blocks before the size marker.
    /// </summary>
    public int ProbeBytes { get; init; } = 128 * 1024;

    /// <summary>Grace window before a never-committed (pending) upload is swept.</summary>
    public int PendingGraceMinutes { get; init; } = 60;

    /// <summary>Grace window before a committed file with no attachment is swept (covers the commit→attach gap).</summary>
    public int UnattachedGraceMinutes { get; init; } = 1440;

    /// <summary>How often the background sweep runs.</summary>
    public int SweepIntervalMinutes { get; init; } = 15;

    /// <summary>Upper bound of files removed per category per sweep run (keeps a run short and lock-friendly).</summary>
    public int SweepBatchSize { get; init; } = 100;

    /// <summary>Maximum files attached to one target (thread/comment).</summary>
    public int MaxAttachmentsPerTarget { get; init; } = 10;
}
