namespace Forum.Infrastructure.Storage;

/// <summary>Object-storage (MinIO/S3) settings, bound from the "Storage" configuration section.</summary>
public sealed class StorageOptions
{
    public string Endpoint { get; init; } = "localhost:9000";

    public string Bucket { get; init; } = "forum";

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public bool UseSsl { get; init; }
}
