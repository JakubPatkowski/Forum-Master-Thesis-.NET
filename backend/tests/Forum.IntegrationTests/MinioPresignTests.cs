using Forum.Infrastructure.Storage;

using Microsoft.Extensions.Options;

using Minio;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// The G5 public-presign split: presigned URLs must be signed against the endpoint the BROWSER will hit
/// (the signature binds the host), while server-side operations keep the in-cluster endpoint. Presigning is
/// pure local computation, so no MinIO container is needed here.
/// </summary>
public sealed class MinioPresignTests
{
    [Fact]
    public async Task Presigned_urls_bind_the_public_endpoint_when_configured()
    {
        var storage = CreateStorage(new StorageOptions
        {
            Endpoint = "minio.internal.test:9000",
            AccessKey = "test-access",
            SecretKey = "test-secret",
            PublicEndpoint = "files.forum.local:9443",
            PublicUseSsl = true,
        });

        var put = new Uri(await storage.PresignPutAsync("2026/07/test-object", TimeSpan.FromMinutes(5)));
        var get = new Uri(await storage.PresignGetAsync("2026/07/test-object", TimeSpan.FromMinutes(5)));

        foreach (var url in new[] { put, get })
        {
            url.Scheme.ShouldBe("https");
            url.Host.ShouldBe("files.forum.local");
            url.Port.ShouldBe(9443);
            url.AbsolutePath.ShouldBe("/forum/2026/07/test-object");
        }
    }

    [Fact]
    public async Task Presigned_urls_default_to_the_internal_endpoint()
    {
        var storage = CreateStorage(new StorageOptions
        {
            Endpoint = "minio.internal.test:9000",
            AccessKey = "test-access",
            SecretKey = "test-secret",
        });

        var put = new Uri(await storage.PresignPutAsync("2026/07/test-object", TimeSpan.FromMinutes(5)));

        put.Scheme.ShouldBe("http");
        put.Host.ShouldBe("minio.internal.test");
        put.Port.ShouldBe(9000);
    }

    private static MinioObjectStorage CreateStorage(StorageOptions options)
    {
        var internalClient = new MinioClient()
            .WithEndpoint(options.Endpoint)
            .WithCredentials(options.AccessKey, options.SecretKey)
            .WithSSL(options.UseSsl)
            .Build();
        return new MinioObjectStorage(internalClient, Options.Create(options));
    }
}
