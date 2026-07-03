using Microsoft.Extensions.Options;

using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace Forum.Infrastructure.Storage;

/// <summary>MinIO-backed <see cref="IObjectStorage"/>. The client is created eagerly but opens no connection until a call is made.</summary>
internal sealed class MinioObjectStorage : IObjectStorage
{
    private readonly IMinioClient _client;
    private readonly StorageOptions _options;

    public MinioObjectStorage(IMinioClient client, IOptions<StorageOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default) =>
        _client.BucketExistsAsync(new BucketExistsArgs().WithBucket(_options.Bucket), cancellationToken);

    public async Task EnsureBucketAsync(CancellationToken cancellationToken = default)
    {
        if (!await BucketExistsAsync(cancellationToken).ConfigureAwait(false))
        {
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_options.Bucket), cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<string> PresignPutAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default) =>
        _client.PresignedPutObjectAsync(new PresignedPutObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithExpiry((int)expiry.TotalSeconds));

    public Task<string> PresignGetAsync(string objectKey, TimeSpan expiry, CancellationToken cancellationToken = default) =>
        _client.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(_options.Bucket)
            .WithObject(objectKey)
            .WithExpiry((int)expiry.TotalSeconds));

    public async Task<ObjectStatResult?> StatAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var stat = await _client.StatObjectAsync(
                new StatObjectArgs().WithBucket(_options.Bucket).WithObject(objectKey),
                cancellationToken).ConfigureAwait(false);

            return new ObjectStatResult(stat.Size, stat.ContentType, stat.ETag);
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task<byte[]?> ReadRangeAsync(string objectKey, int maxBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            using var buffer = new MemoryStream(capacity: maxBytes);
            await _client.GetObjectAsync(
                new GetObjectArgs()
                    .WithBucket(_options.Bucket)
                    .WithObject(objectKey)
                    .WithOffsetAndLength(0, maxBytes)
                    .WithCallbackStream(async (stream, token) =>
                        await stream.CopyToAsync(buffer, token).ConfigureAwait(false)),
                cancellationToken).ConfigureAwait(false);

            return buffer.ToArray();
        }
        catch (ObjectNotFoundException)
        {
            return null;
        }
    }

    public async Task RemoveAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.RemoveObjectAsync(
                new RemoveObjectArgs().WithBucket(_options.Bucket).WithObject(objectKey),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectNotFoundException)
        {
            // Idempotent by contract: the orphan sweep may race a concurrent delete.
        }
    }
}
