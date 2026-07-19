using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Notifications;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Accepts an invite (invitee-only; anyone else reads 404 — an invite's existence is nobody else's business).
/// Deletes the invite, adds membership + chat seat, and pings the inviter's bell. A group deleted since the
/// invite was sent reads 404 and the stale invite is cleaned up on the spot.
/// </summary>
internal sealed record AcceptGroupInviteCommand(Ulid InviteId) : ICommand;

internal sealed class AcceptGroupInviteCommandHandler : ICommandHandler<AcceptGroupInviteCommand>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupInviteRepository _invites;
    private readonly IConversationRepository _conversations;
    private readonly Notifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public AcceptGroupInviteCommandHandler(
        IGroupRepository groups,
        IGroupInviteRepository invites,
        IConversationRepository conversations,
        Notifier notifier,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _invites = invites;
        _conversations = conversations;
        _notifier = notifier;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(AcceptGroupInviteCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var invite = await _invites.GetByIdAsync(command.InviteId, cancellationToken);
        if (invite is null || invite.InvitedUserId != userId)
        {
            return Result.Failure(SocialErrors.InviteNotFound);
        }

        var group = await _groups.GetByIdAsync(invite.GroupId, cancellationToken);
        if (group is null)
        {
            // The group vanished under the invite — drop the dangling row and report the group gone.
            _invites.Remove(invite);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure(SocialErrors.GroupNotFound);
        }

        var now = _clock.GetUtcNow();
        _invites.Remove(invite);

        if (await _groups.GetMembershipAsync(group.Id, userId, cancellationToken) is null)
        {
            await GroupMembershipWriter.AddMemberAsync(
                _groups, _conversations, group.Id, userId, invite.InvitedBy, now, cancellationToken);
            _outbox.Enqueue(new GroupMemberJoinedIntegrationEvent(Ulid.NewUlid(), group.Id, userId, now));
        }

        _notifier.Notify(invite.InvitedBy, NotificationKinds.GroupInviteAccepted, userId, group.Id, now);
        _outbox.Enqueue(new GroupInviteRespondedIntegrationEvent(
            Ulid.NewUlid(), invite.Id, group.Id, userId, invite.InvitedBy, Accepted: true, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
