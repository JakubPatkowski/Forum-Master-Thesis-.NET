using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Testcontainers.PostgreSql;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// Boots the real host against a disposable Postgres and applies the module migrations before tests run. When no
/// Docker engine is reachable it stays <see cref="Available"/> = false so dependent tests skip rather than fail.
/// </summary>
public sealed class ForumApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private PostgreSqlContainer? _database;

    /// <summary>True when the Postgres container started (a Docker engine was reachable).</summary>
    public bool Available { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_database is not null)
        {
            builder.UseSetting("ConnectionStrings:Forum", _database.GetConnectionString());
        }

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

            await _database.StartAsync();

            using var scope = Services.CreateScope();
            foreach (var context in scope.ServiceProvider.GetServices<DbContext>())
            {
                await context.Database.MigrateAsync();
            }

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

        await base.DisposeAsync();
    }
}
