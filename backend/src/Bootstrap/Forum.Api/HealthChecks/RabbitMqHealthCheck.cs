using Forum.Infrastructure.Messaging.RabbitMq;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Forum.Api.HealthChecks;

/// <summary>
/// Readiness probe for RabbitMQ, via the same shared lazy connection the relay and consumer hosts use:
/// reports healthy while the connection is open, and reconnects (or fails) through the wrapper when it is not.
/// </summary>
internal sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IRabbitMqConnection _connection;

    public RabbitMqHealthCheck(IRabbitMqConnection connection) => _connection = connection;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connection.GetConnectionAsync(cancellationToken);
            return connection.IsOpen
                ? HealthCheckResult.Healthy("RabbitMQ is reachable.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is unreachable.", exception);
        }
    }
}
