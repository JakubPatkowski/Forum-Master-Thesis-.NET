using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Application.Sweep;
using Forum.Modules.Files.Contracts.IntegrationEvents;
using Forum.Modules.Files.Domain.Files;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Files.Tests.Unit;

public sealed class OrphanSweeperTests
{
    private readonly IStoredFileRepository _files = Substitute.For<IStoredFileRepository>();
    private readonly IObjectStorage _storage = Substitute.For<IObjectStorage>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISweepLock _sweepLock = Substitute.For<ISweepLock>();

    private OrphanSweeper CreateSweeper() => new(
        _files, _storage, _outbox, _unitOfWork, _sweepLock, TimeProvider.System,
        NullLogger<OrphanSweeper>.Instance, Options.Create(new FilesOptions()));

    private static StoredFile PendingFile() =>
        StoredFile.Create("forum", "image/png", 100, Ulid.NewUlid());

    private static StoredFile CommittedFile()
    {
        var file = PendingFile();
        file.Commit(100, "image/png", 10, 10, DateTimeOffset.UtcNow);
        return file;
    }

    [Fact]
    public async Task Skips_entirely_when_another_replica_holds_the_lock()
    {
        _sweepLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(false);

        var result = await CreateSweeper().SweepAsync(CancellationToken.None);

        result.Acquired.ShouldBeFalse();
        await _files.DidNotReceiveWithAnyArgs()
            .GetExpiredPendingAsync(default, default, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task Sweeps_an_expired_pending_upload_blob_row_and_event()
    {
        var orphan = PendingFile();
        _sweepLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);
        _files.GetExpiredPendingAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([orphan]);
        _files.GetUnattachedCommittedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateSweeper().SweepAsync(CancellationToken.None);

        result.ShouldBe(new SweepResult(true, 1, 0));
        await _storage.Received(1).RemoveAsync(orphan.ObjectKey, Arg.Any<CancellationToken>());
        _files.Received(1).Remove(orphan);
        _outbox.Received(1).Enqueue(Arg.Is<FileOrphanedIntegrationEvent>(orphaned =>
            orphaned.FileId == orphan.Id && orphaned.Reason == "pending_expired"));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sweepLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sweeps_a_committed_file_left_without_attachments()
    {
        var orphan = CommittedFile();
        _sweepLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);
        _files.GetExpiredPendingAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _files.GetUnattachedCommittedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([orphan]);

        var result = await CreateSweeper().SweepAsync(CancellationToken.None);

        result.ShouldBe(new SweepResult(true, 0, 1));
        await _storage.Received(1).RemoveAsync(orphan.ObjectKey, Arg.Any<CancellationToken>());
        _files.Received(1).Remove(orphan);
        _outbox.Received(1).Enqueue(Arg.Is<FileOrphanedIntegrationEvent>(orphaned =>
            orphaned.FileId == orphan.Id && orphaned.Reason == "unattached"));
    }

    [Fact]
    public async Task A_clean_run_saves_nothing_but_still_releases_the_lock()
    {
        _sweepLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);
        _files.GetExpiredPendingAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _files.GetUnattachedCommittedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await CreateSweeper().SweepAsync(CancellationToken.None);

        result.ShouldBe(new SweepResult(true, 0, 0));
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
        await _sweepLock.Received(1).ReleaseAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cutoffs_honor_the_configured_grace_windows()
    {
        var now = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(now);
        _sweepLock.TryAcquireAsync(Arg.Any<CancellationToken>()).Returns(true);
        _files.GetExpiredPendingAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _files.GetUnattachedCommittedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var options = new FilesOptions { PendingGraceMinutes = 30, UnattachedGraceMinutes = 120, SweepBatchSize = 7 };

        var sweeper = new OrphanSweeper(
            _files, _storage, _outbox, _unitOfWork, _sweepLock, clock,
            NullLogger<OrphanSweeper>.Instance, Options.Create(options));
        await sweeper.SweepAsync(CancellationToken.None);

        await _files.Received(1).GetExpiredPendingAsync(now.AddMinutes(-30), 7, Arg.Any<CancellationToken>());
        await _files.Received(1).GetUnattachedCommittedAsync(now.AddMinutes(-120), 7, Arg.Any<CancellationToken>());
    }
}
