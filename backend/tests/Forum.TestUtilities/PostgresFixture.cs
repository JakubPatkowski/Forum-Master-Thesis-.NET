using Testcontainers.PostgreSql;

using Xunit;

namespace Forum.TestUtilities;

/// <summary>
/// Spins up a disposable PostgreSQL container for integration tests. When no Docker engine is reachable the fixture
/// stays <see cref="Available"/> = false instead of throwing, so dependent tests can skip rather than fail.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    /// <summary>True when a container was started (a Docker engine was reachable).</summary>
    public bool Available { get; private set; }

    public string ConnectionString => _container?.GetConnectionString() ?? string.Empty;

    public async Task InitializeAsync()
    {
        try
        {
            _container = new PostgreSqlBuilder()
                .WithImage("postgres:17-alpine")
                .WithDatabase("forum_net_test")
                .WithUsername("forum")
                .WithPassword("forum")
                .Build();

            await _container.StartAsync();
            Available = true;
        }
        catch (Exception)
        {
            // No Docker engine reachable — leave Available = false; tests using this fixture will be skipped.
            Available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
