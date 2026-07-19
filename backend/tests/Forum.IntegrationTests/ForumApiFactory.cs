using System.Globalization;
using System.Net;
using System.Net.Sockets;

using Forum.Infrastructure.Storage;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.Minio;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// Boots the real host against disposable Postgres + MinIO + RabbitMQ containers and applies the module
/// migrations before tests run. When no Docker engine is reachable it stays <see cref="Available"/> = false so
/// dependent tests skip rather than fail.
/// </summary>
public class ForumApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _database;
    private MinioContainer? _minio;
    private RabbitMqContainer? _rabbitMq;

    /// <summary>True when the containers started (a Docker engine was reachable).</summary>
    public bool Available { get; private set; }

    /// <summary>The broker's AMQP connection string (for tests that publish raw messages themselves).</summary>
    public string RabbitMqConnectionString => _rabbitMq?.GetConnectionString() ?? string.Empty;

    /// <summary>Stops the broker container so readiness tests can watch <c>/health/ready</c> flip.</summary>
    public Task StopRabbitMqAsync() => _rabbitMq?.StopAsync() ?? Task.CompletedTask;

    /// <summary>Restarts the broker container; the host port stays fixed, so the host reconnects on its own.</summary>
    public Task StartRabbitMqAsync() => _rabbitMq?.StartAsync() ?? Task.CompletedTask;

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

        if (_rabbitMq is not null)
        {
            builder.UseSetting("RabbitMq:Host", _rabbitMq.Hostname);
            builder.UseSetting(
                "RabbitMq:Port", _rabbitMq.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RabbitMq:Username", "forum");
            builder.UseSetting("RabbitMq:Password", "forum");
        }

        // Fast messaging cadence so outbox → RabbitMQ → consumer round-trips settle within a polling assert.
        builder.UseSetting("Messaging:PollIntervalMilliseconds", "200");
        builder.UseSetting("Messaging:RetryDelayMilliseconds", "250");
        builder.UseSetting("Messaging:MaxDeliveryAttempts", "3");

        // Zero grace windows: the sweep only runs when a test invokes it explicitly (the background timer's
        // first tick lies far beyond any test run), so "already sweepable" makes those invocations deterministic.
        builder.UseSetting("Files:PendingGraceMinutes", "0");
        builder.UseSetting("Files:UnattachedGraceMinutes", "0");

        builder.UseSetting("Jwt:SigningKey", "forum-integration-tests-signing-key-0123456789-abcdefghijklmnop");

        // Every test request shares one "IP" — raise the per-IP limits so suites don't trip 429s.
        builder.UseSetting("RateLimiting:Global:PermitLimit", "10000");
        builder.UseSetting("RateLimiting:Auth:PermitLimit", "1000");

        // The Prometheus exporter's default 10s scrape-response cache would make polling assertions flake
        // (a stale cached body keeps getting served no matter how long a test waits) — force a fresh collect
        // on every /metrics call in tests.
        builder.UseSetting("Observability:PrometheusScrapeCacheMs", "0");
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

            _rabbitMq = ConfigureRabbitMq(new RabbitMqBuilder()
                .WithImage("rabbitmq:4-management")  // same image as compose.yaml / the cluster
                .WithUsername("forum")
                .WithPassword("forum"))
                .Build();

            await Task.WhenAll(_database.StartAsync(), _minio.StartAsync(), _rabbitMq.StartAsync());

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

    /// <summary>Hook for tests that need a specially configured broker (e.g. a fixed host port to survive restarts).</summary>
    protected virtual RabbitMqBuilder ConfigureRabbitMq(RabbitMqBuilder builder) => builder;

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

        if (_rabbitMq is not null)
        {
            await _rabbitMq.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>Reserves an ephemeral port for containers that must keep their host port across restarts.</summary>
    protected static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
