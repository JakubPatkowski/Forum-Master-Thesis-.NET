using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Removes a pending invite — decline (invitee) and cancel (inviter) are the same deletion; anyone else reads
/// 404. Silent for the other party's bell, but open views update off the integration event.
/// </summary>
internal sealed record DeleteGroupInviteCommand(Ulid InviteId) : ICommand;

internal sealed class DeleteGroupInviteCommandHandler : ICommandHandler<DeleteGroupInviteCommand>
{
    private readonly IGroupInviteRepository _invites;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public DeleteGroupInviteCommandHandler(
        IGroupInviteRepository invites,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _invites = invites;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(DeleteGroupInviteCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var invite = await _invites.GetByIdAsync(command.InviteId, cancellationToken);
        if (invite is null || (invite.InvitedUserId != userId && invite.InvitedBy != userId))
        {
            return Result.Failure(SocialErrors.InviteNotFound);
        }

        _invites.Remove(invite);
        _outbox.Enqueue(new GroupInviteRespondedIntegrationEvent(
            Ulid.NewUlid(), invite.Id, invite.GroupId, invite.InvitedUserId, invite.InvitedBy,
            Accepted: false, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
