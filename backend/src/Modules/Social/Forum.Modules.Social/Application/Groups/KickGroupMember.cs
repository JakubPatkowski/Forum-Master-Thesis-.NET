using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Groups;
using Forum.Modules.Social.Domain.Notifications;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Removes a member (manage-gate; the owner is unkickable). The removed user gets a durable notification; their
/// admin grant (if any) is revoked so a future re-join starts as a plain member.
/// </summary>
internal sealed record KickGroupMemberCommand(Ulid GroupId, Ulid UserId) : ICommand;

internal sealed class KickGroupMemberCommandHandler : ICommandHandler<KickGroupMemberCommand>
{
    private readonly IGroupRepository _groups;
    private readonly IConversationRepository _conversations;
    private readonly IAclGrantService _aclGrants;
    private readonly Notifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public KickGroupMemberCommandHandler(
        IGroupRepository groups,
        IConversationRepository conversations,
        IAclGrantService aclGrants,
        Notifier notifier,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _conversations = conversations;
        _aclGrants = aclGrants;
        _notifier = notifier;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(KickGroupMemberCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } actorId)
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

        var membership = await _groups.GetMembershipAsync(group.Id, command.UserId, cancellationToken);
        if (membership is null)
        {
            return Result.Failure(SocialErrors.MembershipNotFound);
        }

        if (group.OwnerId == command.UserId)
        {
            return Result.Failure(GroupErrors.OwnerCannotBeKicked);
        }

        var now = _clock.GetUtcNow();
        await GroupMembershipWriter.RemoveMemberAsync(_groups, _conversations, membership, now, cancellationToken);
        _notifier.Notify(command.UserId, NotificationKinds.GroupKicked, actorId, group.Id, now);
        _outbox.Enqueue(new GroupMemberLeftIntegrationEvent(
            Ulid.NewUlid(), group.Id, command.UserId, Removed: true, RemovedBy: actorId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _aclGrants.RevokeAsync(
            command.UserId, Permissions.Moderate, PermissionScopes.Group, group.Id, cancellationToken);
        return Result.Success();
    }
}
