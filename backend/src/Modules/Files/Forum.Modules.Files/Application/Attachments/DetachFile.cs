using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Social.Contracts;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Files.Application.Attachments;

/// <summary>
/// Removes a file ↔ target link. The uploader may always unlink their own file; anyone else must pass the
/// target-owning module's gate (e.g. a moderator detaching an image from a thread they moderate). Idempotent:
/// detaching a link that does not exist succeeds.
/// </summary>
internal sealed record DetachFileCommand(Ulid FileId, string TargetType, Ulid? TargetId) : ICommand;

internal sealed class DetachFileCommandHandler : ICommandHandler<DetachFileCommand>
{
    private readonly IStoredFileRepository _files;
    private readonly IContentAuthorization _contentAuthorization;
    private readonly ISocialAuthorization _socialAuthorization;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public DetachFileCommandHandler(
        IStoredFileRepository files,
        IContentAuthorization contentAuthorization,
        ISocialAuthorization socialAuthorization,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
    {
        _files = files;
        _contentAuthorization = contentAuthorization;
        _socialAuthorization = socialAuthorization;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DetachFileCommand command, CancellationToken cancellationToken)
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

        if (!FileTargets.TryParse(command.TargetType, out var targetType))
        {
            return Result.Failure(FileErrors.InvalidTargetType);
        }

        var targetId = command.TargetId ?? (targetType == FileTargetType.Avatar ? userId : default);
        if (targetId == default)
        {
            return Result.Failure(FileErrors.TargetRequired);
        }

        if (file.OwnerId != userId)
        {
            // Not the uploader — the target's owning module decides (moderator powers live there).
            Result authorized;
            if (FileTargets.ToContentTarget(targetType) is { } contentTarget)
            {
                authorized = await _contentAuthorization.AuthorizeAttachmentAsync(
                    contentTarget, targetId, userId, cancellationToken);
            }
            else if (FileTargets.ToSocialTarget(targetType) is { } socialTarget)
            {
                authorized = await _socialAuthorization.AuthorizeAttachmentAsync(
                    socialTarget, targetId, userId, cancellationToken);
            }
            else
            {
                return Result.Failure(FileErrors.NotOwner);
            }

            if (authorized.IsFailure)
            {
                return authorized;
            }
        }

        file.Detach(targetType, targetId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
