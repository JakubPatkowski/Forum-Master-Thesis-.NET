using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Application.Imaging;
using Forum.Modules.Files.Contracts.IntegrationEvents;
using Forum.Modules.Files.Domain.Files;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Files;

/// <summary>
/// Step 3 of the direct-to-MinIO flow (ADR 0008): stats the stored object for its REAL size, sniffs the REAL
/// content type from the object's magic bytes and decodes dimensions — the declared values are never trusted —
/// then flips the row to committed. Re-committing an already-committed file is an idempotent success (retry-safe).
/// </summary>
internal sealed record CommitUploadCommand(Ulid FileId) : ICommand<CommitUploadResponse>;

internal sealed record CommitUploadResponse(
    Ulid FileId, string ContentType, long SizeBytes, int? Width, int? Height);

internal sealed class CommitUploadCommandHandler : ICommandHandler<CommitUploadCommand, CommitUploadResponse>
{
    private readonly IStoredFileRepository _files;
    private readonly IObjectStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;

    public CommitUploadCommandHandler(
        IStoredFileRepository files,
        IObjectStorage storage,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IOptions<FilesOptions> options)
    {
        _files = files;
        _storage = storage;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<CommitUploadResponse>> Handle(
        CommitUploadCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<CommitUploadResponse>(FilesErrors.AuthenticationRequired);
        }

        var file = await _files.GetByIdAsync(command.FileId, cancellationToken);
        if (file is null)
        {
            return Result.Failure<CommitUploadResponse>(FileErrors.NotFound);
        }

        if (file.OwnerId != userId)
        {
            return Result.Failure<CommitUploadResponse>(FileErrors.NotOwner);
        }

        if (file.Status == FileStatus.Committed)
        {
            return Result.Success(ToResponse(file)); // Retry-safe: the commit already happened.
        }

        var stat = await _storage.StatAsync(file.ObjectKey, cancellationToken);
        if (stat is null)
        {
            return Result.Failure<CommitUploadResponse>(FileErrors.NotUploaded);
        }

        var header = await _storage.ReadRangeAsync(file.ObjectKey, _options.ProbeBytes, cancellationToken);
        if (header is null)
        {
            return Result.Failure<CommitUploadResponse>(FileErrors.NotUploaded); // Raced a concurrent delete.
        }

        if (!ImageProbe.TryIdentify(header, out var identity))
        {
            return Result.Failure<CommitUploadResponse>(FileErrors.NotADecodableImage);
        }

        var committed = file.Commit(stat.SizeBytes, identity.ContentType, identity.Width, identity.Height, _clock.GetUtcNow());
        if (committed.IsFailure)
        {
            return Result.Failure<CommitUploadResponse>(committed.Error);
        }

        _outbox.Enqueue(new FileCommittedIntegrationEvent(
            Ulid.NewUlid(), file.Id, file.OwnerId, file.ContentType, file.SizeBytes,
            identity.Width, identity.Height, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(file));
    }

    private static CommitUploadResponse ToResponse(StoredFile file) =>
        new(file.Id, file.ContentType, file.SizeBytes, file.Width, file.Height);
}
