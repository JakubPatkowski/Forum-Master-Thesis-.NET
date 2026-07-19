using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Social.Contracts;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Attachments;

/// <summary>
/// Links a committed file to a target. This is the authorization-sensitive step of the upload flow:
/// only the uploader may attach their file, and the module that owns the target decides whether the user
/// may modify it — thread/comment/category targets are gated by Content's <see cref="IContentAuthorization"/>
/// contract, message/group-icon targets by Social's <see cref="ISocialAuthorization"/> mirror (owner-or-privileged,
/// 404 → 403 preserved either way), and an avatar is self-authorized (target = the requesting user). Avatars,
/// category/thread/group icons use replace semantics (one live attachment per target); threads/comments/messages
/// are additive up to the cap.
/// </summary>
internal sealed record AttachFileCommand(Ulid FileId, string TargetType, Ulid? TargetId) : ICommand;

internal sealed class AttachFileCommandHandler : ICommandHandler<AttachFileCommand>
{
    private readonly IStoredFileRepository _files;
    private readonly IContentAuthorization _contentAuthorization;
    private readonly ISocialAuthorization _socialAuthorization;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;

    public AttachFileCommandHandler(
        IStoredFileRepository files,
        IContentAuthorization contentAuthorization,
        ISocialAuthorization socialAuthorization,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IOptions<FilesOptions> options)
    {
        _files = files;
        _contentAuthorization = contentAuthorization;
        _socialAuthorization = socialAuthorization;
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
            if (command.TargetId is not { } externalTargetId)
            {
                return Result.Failure(FileErrors.TargetRequired);
            }

            targetId = externalTargetId;

            // The target's owning module holds the rules; we only consume the verdict (404 → 403 preserved).
            var authorized = FileTargets.ToContentTarget(targetType) is { } contentTarget
                ? await _contentAuthorization.AuthorizeAttachmentAsync(
                    contentTarget, targetId, userId, cancellationToken)
                : await _socialAuthorization.AuthorizeAttachmentAsync(
                    FileTargets.ToSocialTarget(targetType)!.Value, targetId, userId, cancellationToken);
            if (authorized.IsFailure)
            {
                return authorized;
            }
        }

        if (file.IsAttachedTo(targetType, targetId))
        {
            return Result.Success(); // Idempotent: the link already exists.
        }

        if (targetType is FileTargetType.Avatar or FileTargetType.CategoryIcon or FileTargetType.ThreadIcon
            or FileTargetType.GroupIcon)
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
