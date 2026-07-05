using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace Forum.Api.HealthChecks;

/// <summary>Readiness probe for PostgreSQL: opens a pooled connection and runs <c>SELECT 1</c>.</summary>
internal sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly string _connectionString;

    public PostgresHealthCheck(IConfiguration configuration) =>
        _connectionString = configuration.GetConnectionString("Forum") ?? string.Empty;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is unreachable.", exception);
        }
    }
}
