using Forum.Modules.Files.Application;
using Forum.Modules.Files.Application.Sweep;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Infrastructure.Sweep;

/// <summary>
/// Periodic driver of the orphan sweep. A recurring job, so it is a <see cref="BackgroundService"/> —
/// deliberately NOT an <c>IStartupTask</c> (those are ordered one-shot boot work). Each tick runs the scoped
/// <see cref="OrphanSweeper"/>, which itself no-ops when another replica holds the advisory sweep lock.
/// The first tick fires one interval after boot, so startup stays lean.
/// </summary>
internal sealed class OrphanSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrphanSweepService> _logger;
    private readonly FilesOptions _options;

    public OrphanSweepService(
        IServiceScopeFactory scopeFactory, ILogger<OrphanSweepService> logger, IOptions<FilesOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.SweepIntervalMinutes));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var sweeper = scope.ServiceProvider.GetRequiredService<OrphanSweeper>();
                    await sweeper.SweepAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A failed sweep is retried on the next tick; never crash the host over garbage collection.
                    _logger.LogError(exception, "Orphan sweep run failed; retrying next interval.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }
}
