using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Groups;
using Forum.Modules.Social.Domain.Notifications;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Invites a user (manage-gate — inviting is admin-level per the design split; ordinary members do not fan out
/// invites). Order: group 404 → inviter rights 403 → invitee 404 → block/privacy 403 → conflicts 409. The
/// invitee gets a durable notification + realtime ping.
/// </summary>
internal sealed record InviteToGroupCommand(Ulid GroupId, Ulid UserId) : ICommand<InviteToGroupResponse>;

internal sealed record InviteToGroupResponse(Ulid InviteId);

internal sealed class InviteToGroupCommandHandler : ICommandHandler<InviteToGroupCommand, InviteToGroupResponse>
{
    private readonly IGroupRepository _groups;
    private readonly IGroupInviteRepository _invites;
    private readonly IUserReader _users;
    private readonly SocialInteractionGate _gate;
    private readonly Notifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public InviteToGroupCommandHandler(
        IGroupRepository groups,
        IGroupInviteRepository invites,
        IUserReader users,
        SocialInteractionGate gate,
        Notifier notifier,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _invites = invites;
        _users = users;
        _gate = gate;
        _notifier = notifier;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<InviteToGroupResponse>> Handle(
        InviteToGroupCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } inviterId)
        {
            return Result.Failure<InviteToGroupResponse>(SocialErrors.AuthenticationRequired);
        }

        var group = await _groups.GetByIdAsync(command.GroupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure<InviteToGroupResponse>(SocialErrors.GroupNotFound);
        }

        if (!await GroupGuards.MayManageAsync(_currentUser, group, cancellationToken))
        {
            return Result.Failure<InviteToGroupResponse>(SocialErrors.GroupForbidden);
        }

        if (!await _users.IsActiveAsync(command.UserId, cancellationToken))
        {
            return Result.Failure<InviteToGroupResponse>(SocialErrors.UserNotFound);
        }

        var allowed = await _gate.MayInviteToGroupAsync(inviterId, command.UserId, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<InviteToGroupResponse>(allowed.Error);
        }

        if (await _groups.GetMembershipAsync(group.Id, command.UserId, cancellationToken) is not null)
        {
            return Result.Failure<InviteToGroupResponse>(SocialErrors.AlreadyGroupMember);
        }

        if (await _invites.GetPendingAsync(group.Id, command.UserId, cancellationToken) is not null)
        {
            return Result.Failure<InviteToGroupResponse>(SocialErrors.AlreadyInvited);
        }

        var now = _clock.GetUtcNow();
        var invite = GroupInvite.Create(group.Id, command.UserId, inviterId);
        _invites.Add(invite);
        _notifier.Notify(command.UserId, NotificationKinds.GroupInvite, inviterId, invite.Id, now);
        _outbox.Enqueue(new GroupInviteSentIntegrationEvent(
            Ulid.NewUlid(), invite.Id, group.Id, command.UserId, inviterId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new InviteToGroupResponse(invite.Id));
    }
}
