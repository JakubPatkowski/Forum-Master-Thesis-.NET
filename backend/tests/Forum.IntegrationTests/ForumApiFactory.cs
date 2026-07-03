using Forum.Infrastructure.Storage;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.Minio;
using Testcontainers.PostgreSql;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// Boots the real host against disposable Postgres + MinIO containers and applies the module migrations before
/// tests run. When no Docker engine is reachable it stays <see cref="Available"/> = false so dependent tests
/// skip rather than fail.
/// </summary>
public sealed class ForumApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _database;
    private MinioContainer? _minio;

    /// <summary>True when the containers started (a Docker engine was reachable).</summary>
    public bool Available { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_database is not null)
        {
            builder.UseSetting("ConnectionStrings:Forum", _database.GetConnectionString());
        }

        if (_minio is not null)
        {
            builder.UseSetting("Storage:Endpoint", new Uri(_minio.GetConnectionString()).Authority);
            builder.UseSetting("Storage:AccessKey", _minio.GetAccessKey());
            builder.UseSetting("Storage:SecretKey", _minio.GetSecretKey());
            builder.UseSetting("Storage:Bucket", "forum");
            builder.UseSetting("Storage:UseSsl", "false");
        }

        // Zero grace windows: the sweep only runs when a test invokes it explicitly (the background timer's
        // first tick lies far beyond any test run), so "already sweepable" makes those invocations deterministic.
        builder.UseSetting("Files:PendingGraceMinutes", "0");
        builder.UseSetting("Files:UnattachedGraceMinutes", "0");

        builder.UseSetting("Jwt:SigningKey", "forum-integration-tests-signing-key-0123456789-abcdefghijklmnop");

        // Every test request shares one "IP" — raise the per-IP limits so suites don't trip 429s.
        builder.UseSetting("RateLimiting:Global:PermitLimit", "10000");
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "1000");
    }

    public async Task InitializeAsync()
    {
        try
        {
            _database = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("forum_net")
                .WithUsername("forum")
                .WithPassword("forum")
                .Build();

            _minio = new MinioBuilder().Build();

            await Task.WhenAll(_database.StartAsync(), _minio.StartAsync());

            using var scope = Services.CreateScope();
            foreach (var context in scope.ServiceProvider.GetServices<DbContext>())
            {
                await context.Database.MigrateAsync();
            }

            // The bucket is pre-created out-of-band in dev/cluster (infra-up.sh / k8s); tests bootstrap it here.
            await scope.ServiceProvider.GetRequiredService<IObjectStorage>().EnsureBucketAsync();

            Available = true;
        }
        catch (Exception)
        {
            // No Docker engine reachable — leave Available = false; tests using this factory will be skipped.
            Available = false;
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        if (_minio is not null)
        {
            await _minio.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
