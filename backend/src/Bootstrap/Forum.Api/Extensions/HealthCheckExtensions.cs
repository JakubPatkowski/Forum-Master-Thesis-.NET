using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Forum.Api.Extensions;

public static class HealthCheckExtensions
{
    private const string Ready = "ready";

    public static IServiceCollection AddForumHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks();
        // TODO: .AddNpgSql(...).AddRabbitMQ(...) tagged "ready" for readiness gating.
        return services;
    }

    public static WebApplication MapForumHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = static check => check.Tags.Contains(Ready) });
        return app;
    }
}
