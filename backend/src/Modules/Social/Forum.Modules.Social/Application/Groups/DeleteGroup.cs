using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Soft-deletes a group (owner or manage-gate). Memberships/participants/messages stay in place — the soft-delete
/// query filter and the views hide the group everywhere; Files detaches the icon off the integration event.
/// </summary>
internal sealed record DeleteGroupCommand(Ulid GroupId) : ICommand;

internal sealed class DeleteGroupCommandHandler : ICommandHandler<DeleteGroupCommand>
{
    private readonly IGroupRepository _groups;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public DeleteGroupCommandHandler(
        IGroupRepository groups,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(DeleteGroupCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var group = await _groups.GetByIdAsync(command.GroupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure(SocialErrors.GroupNotFound);
        }

        if (!await GroupGuards.MayManageAsync(_currentUser, group, cancellationToken))
        {
            return Result.Failure(SocialErrors.GroupForbidden);
        }

        var now = _clock.GetUtcNow();
        var deleted = group.Delete(userId, now);
        if (deleted.IsFailure)
        {
            return deleted;
        }

        _outbox.Enqueue(new GroupDeletedIntegrationEvent(Ulid.NewUlid(), group.Id, userId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
