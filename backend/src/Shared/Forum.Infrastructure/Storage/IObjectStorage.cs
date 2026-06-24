namespace Forum.Infrastructure.Storage;

/// <summary>Thin abstraction over the S3/MinIO object store. Presigned upload/download URLs are added in Phase 3.</summary>
public interface IObjectStorage
{
    /// <summary>Returns whether the configured bucket exists. Used by readiness checks and the bucket bootstrap.</summary>
    Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default);
}
