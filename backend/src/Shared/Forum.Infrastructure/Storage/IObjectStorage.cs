namespace Forum.Infrastructure.Storage;

/// <summary>
/// Thin abstraction over the S3/MinIO object store, kept free of any module-specific types. The control plane
/// only presigns URLs and inspects metadata — object bytes never transit the backend (ADR 0008).
/// </summary>
public interface IObjectStorage
{
    /// <summary>Returns whether the configured bucket exists. Used by readiness checks and the bucket bootstrap.</summary>
    Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates the configured bucket when it does not exist yet (dev/test bootstrap; production pre-creates it).</summary>
    Task EnsureBucketAsync(CancellationToken cancellationToken = default);

    /// <summary>Presigns a PUT of <paramref name="objectKey"/> so the client uploads bytes straight to the store.</summary>
    Task<string> PresignPutAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>Presigns a GET of <paramref name="objectKey"/> so the client downloads bytes straight from the store.</summary>
    Task<string> PresignGetAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>Stats the stored object (real size/type as the store sees it), or null when it was never uploaded.</summary>
    Task<ObjectStatResult?> StatAsync(string objectKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads at most <paramref name="maxBytes"/> from the start of the object (enough to sniff magic bytes and
    /// decode image dimensions without pulling the whole blob), or null when the object does not exist.
    /// </summary>
    Task<byte[]?> ReadRangeAsync(string objectKey, int maxBytes, CancellationToken cancellationToken = default);

    /// <summary>Removes the object. Idempotent: removing a missing object is a no-op, never an error.</summary>
    Task RemoveAsync(string objectKey, CancellationToken cancellationToken = default);
}
