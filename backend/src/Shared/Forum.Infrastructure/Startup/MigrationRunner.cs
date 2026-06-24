using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Infrastructure.Startup;

public static class MigrationRunner
{
    /// <summary>
    /// Applies every registered module DbContext's migrations, then exits. Invoked by the `migrate` entrypoint arg and
    /// run as a one-shot Kubernetes Job before rollout (ADR 0005), never at pod startup. No-op until a module registers a context.
    /// </summary>
    public static async Task RunMigrationsAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        using var scope = app.Services.CreateScope();
        foreach (var context in scope.ServiceProvider.GetServices<DbContext>())
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
