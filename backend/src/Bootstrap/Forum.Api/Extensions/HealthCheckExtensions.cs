using Forum.Api.HealthChecks;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Forum.Api.Extensions;

public static class HealthCheckExtensions
{
    private const string Ready = "ready";

    public static IServiceCollection AddForumHealthChecks(this IServiceCollection services)
    {
        // Liveness stays dependency-free; readiness gates on the stateful dependencies being reachable.
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", tags: [Ready])
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", tags: [Ready]);
        return services;
    }

    public static WebApplication MapForumHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static check => check.Tags.Contains(Ready) });
        return app;
    }
}
