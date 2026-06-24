using Testcontainers.PostgreSql;

using Xunit;

namespace Forum.TestUtilities;

/// <summary>Spins up a disposable PostgreSQL container for integration tests that need a real database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("forum_net_test")
        .WithUsername("forum")
        .WithPassword("forum")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
