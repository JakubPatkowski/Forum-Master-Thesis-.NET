using System.Diagnostics;

using Forum.Infrastructure.Seeding;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Forum.Infrastructure.Startup;

/// <summary>
/// Runs every registered <see cref="IModuleSeeder"/> in order, then exits. Invoked by the <c>seed</c> entrypoint
/// arg (parallel to <see cref="MigrationRunner"/>'s <c>migrate</c>) and run as a one-shot Job in k8s — deliberately
/// NOT an <see cref="IStartupTask"/>, so it never fires on an ordinary <c>dotnet run</c>.
/// </summary>
public static class SeedRunner
{
    /// <summary>Seeds via the application's service provider, honouring host shutdown as the cancellation signal.</summary>
    public static Task RunSeedAsync(this WebApplication app, SeedConfig config) =>
        SeedAsync(app.Services, config, app.Lifetime.ApplicationStopping);

    /// <summary>
    /// Core seed entrypoint against any provider (also used by the integration test). Resolves the module seeders
    /// in a fresh scope and runs them sequentially inside a stopwatch + summary log.
    /// </summary>
    public static async Task SeedAsync(
        IServiceProvider services, SeedConfig config, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SeedRunner));

        var seeders = provider.GetServices<IModuleSeeder>().OrderBy(static seeder => seeder.Order).ToArray();
        if (seeders.Length == 0)
        {
            logger.LogWarning("No module seeders are registered — nothing to seed.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Seeding profile {Profile} (force={Force}) with {SeederCount} module seeder(s)…",
                config.Profile, config.AllowTruncate, seeders.Length);
        }

        foreach (var seeder in seeders)
        {
            await seeder.SeedAsync(config, cancellationToken).ConfigureAwait(false);
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Seed complete: profile {Profile} in {Elapsed}.", config.Profile, stopwatch.Elapsed);
        }
    }
}
