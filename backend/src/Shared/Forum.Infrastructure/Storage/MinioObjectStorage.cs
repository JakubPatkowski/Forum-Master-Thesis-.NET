using Microsoft.Extensions.Options;

using Minio;
using Minio.DataModel.Args;

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
}
