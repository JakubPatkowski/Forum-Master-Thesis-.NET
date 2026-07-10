using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Attachments;

/// <summary>
/// Links a committed file to a target. This is the authorization-sensitive step of the upload flow:
/// only the uploader may attach their file, and the module that owns the target decides whether the user
/// may modify it — thread/comment/category targets are gated by Content's <see cref="IContentAuthorization"/>
/// contract (owner-or-moderator, 404 → 403 preserved), an avatar is self-authorized (target = the requesting
/// user), and DM targets are rejected until the Social module lands (Phase 5). Avatars, category icons and thread
/// icons use replace semantics (one live attachment per target); threads/comments are additive up to the cap.
/// </summary>
internal sealed record AttachFileCommand(Ulid FileId, string TargetType, Ulid? TargetId) : ICommand;

internal sealed class AttachFileCommandHandler : ICommandHandler<AttachFileCommand>
{
    private readonly IStoredFileRepository _files;
    private readonly IContentAuthorization _contentAuthorization;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;

    public AttachFileCommandHandler(
        IStoredFileRepository files,
        IContentAuthorization contentAuthorization,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IOptions<FilesOptions> options)
    {
        _files = files;
        _contentAuthorization = contentAuthorization;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result> Handle(AttachFileCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(FilesErrors.AuthenticationRequired);
        }

        var file = await _files.GetByIdAsync(command.FileId, cancellationToken);
        if (file is null)
        {
            return Result.Failure(FileErrors.NotFound);
        }

        if (file.OwnerId != userId)
        {
            return Result.Failure(FileErrors.NotOwner);
        }

        if (file.Status != FileStatus.Committed)
        {
            return Result.Failure(FileErrors.NotCommitted);
        }

        if (!FileTargets.TryParse(command.TargetType, out var targetType))
        {
            return Result.Failure(FileErrors.InvalidTargetType);
        }

        if (targetType == FileTargetType.Dm)
        {
            return Result.Failure(FileErrors.DmAttachmentsNotSupported);
        }

        Ulid targetId;
        if (targetType == FileTargetType.Avatar)
        {
            targetId = command.TargetId ?? userId;
            if (targetId != userId)
            {
                return Result.Failure(FileErrors.AvatarTargetMismatch);
            }
        }
        else
        {
            if (command.TargetId is not { } contentTargetId)
            {
                return Result.Failure(FileErrors.TargetRequired);
            }

            targetId = contentTargetId;

            // Content owns the target's rules; we only consume the verdict (404 → 403 order preserved).
            var authorized = await _contentAuthorization.AuthorizeAttachmentAsync(
                FileTargets.ToContentTarget(targetType)!.Value, targetId, userId, cancellationToken);
            if (authorized.IsFailure)
            {
                return authorized;
            }
        }

        if (file.IsAttachedTo(targetType, targetId))
        {
            return Result.Success(); // Idempotent: the link already exists.
        }

        if (targetType is FileTargetType.Avatar or FileTargetType.CategoryIcon or FileTargetType.ThreadIcon)
        {
            // Single-slot targets: attaching a new avatar/icon replaces the previous one, which the
            // orphan sweep removes once its grace window passes (if nothing else references it).
            var currentlyAttached = await _files.GetAttachedToTargetAsync(targetType, targetId, cancellationToken);
            foreach (var attached in currentlyAttached)
            {
                attached.Detach(targetType, targetId);
            }
        }
        else if (await _files.CountAttachmentsForTargetAsync(targetType, targetId, cancellationToken)
                 >= _options.MaxAttachmentsPerTarget)
        {
            return Result.Failure(FileErrors.TooManyAttachments);
        }

        var attach = file.Attach(targetType, targetId, _clock.GetUtcNow());
        if (attach.IsFailure)
        {
            return attach;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
