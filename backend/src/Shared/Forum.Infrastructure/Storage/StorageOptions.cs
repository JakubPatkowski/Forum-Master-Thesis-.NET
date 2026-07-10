namespace Forum.Infrastructure.Storage;

/// <summary>Object-storage (MinIO/S3) settings, bound from the "Storage" configuration section.</summary>
public sealed class StorageOptions
{
    public string Endpoint { get; init; } = "localhost:9000";

    public string Bucket { get; init; } = "forum";

    public string AccessKey { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public bool UseSsl { get; init; }

    /// <summary>
    /// The endpoint browsers reach MinIO on (e.g. the ingress host in k8s). Presigned URLs must be signed
    /// against it — the signature binds the host — while server-side operations keep using <see cref="Endpoint"/>.
    /// Null (the default) presigns against <see cref="Endpoint"/>, which is correct for dev/compose.
    /// </summary>
    public string? PublicEndpoint { get; init; }

    public bool PublicUseSsl { get; init; }
}
