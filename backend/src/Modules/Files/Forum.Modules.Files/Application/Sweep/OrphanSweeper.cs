using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Contracts.IntegrationEvents;
using Forum.Modules.Files.Domain.Files;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Sweep;

/// <summary>Outcome of one sweep run. <see cref="Acquired"/> is false when another replica held the sweep lock.</summary>
internal sealed record SweepResult(bool Acquired, int PendingSwept, int UnattachedSwept);

/// <summary>
/// The orphan sweep (ADR 0008): physically removes (blob first, then row — both idempotent, so a crash between
/// the two just retries next run) pending uploads that outlived their grace window and committed files left with
/// no attachment. Runs under a cross-replica lock; each removal writes a <see cref="FileOrphanedIntegrationEvent"/>
/// outbox row in the same transaction as the row deletes.
/// </summary>
internal sealed class OrphanSweeper
{
    private readonly IStoredFileRepository _files;
    private readonly IObjectStorage _storage;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISweepLock _sweepLock;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrphanSweeper> _logger;
    private readonly FilesOptions _options;

    public OrphanSweeper(
        IStoredFileRepository files,
        IObjectStorage storage,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        ISweepLock sweepLock,
        TimeProvider clock,
        ILogger<OrphanSweeper> logger,
        IOptions<FilesOptions> options)
    {
        _files = files;
        _storage = storage;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _sweepLock = sweepLock;
        _clock = clock;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<SweepResult> SweepAsync(CancellationToken cancellationToken)
    {
        if (!await _sweepLock.TryAcquireAsync(cancellationToken))
        {
            return new SweepResult(false, 0, 0);
        }

        try
        {
            var now = _clock.GetUtcNow();

            var expiredPending = await _files.GetExpiredPendingAsync(
                now.AddMinutes(-_options.PendingGraceMinutes), _options.SweepBatchSize, cancellationToken);
            foreach (var file in expiredPending)
            {
                await RemoveAsync(file, "pending_expired", now, cancellationToken);
            }

            var unattached = await _files.GetUnattachedCommittedAsync(
                now.AddMinutes(-_options.UnattachedGraceMinutes), _options.SweepBatchSize, cancellationToken);
            foreach (var file in unattached)
            {
                await RemoveAsync(file, "unattached", now, cancellationToken);
            }

            if (expiredPending.Count > 0 || unattached.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation(
                        "Orphan sweep removed {Pending} expired pending and {Unattached} unattached committed file(s).",
                        expiredPending.Count, unattached.Count);
                }
            }

            return new SweepResult(true, expiredPending.Count, unattached.Count);
        }
        finally
        {
            await _sweepLock.ReleaseAsync(CancellationToken.None);
        }
    }

    private async Task RemoveAsync(StoredFile file, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await _storage.RemoveAsync(file.ObjectKey, cancellationToken);
        _files.Remove(file);
        _outbox.Enqueue(new FileOrphanedIntegrationEvent(
            Ulid.NewUlid(), file.Id, file.OwnerId, file.ObjectKey, reason, now));
    }
}
