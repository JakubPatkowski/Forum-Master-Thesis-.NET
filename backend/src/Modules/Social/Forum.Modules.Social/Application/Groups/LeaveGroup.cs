using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Groups;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Leaves a group. The owner cannot leave (422) — transfer ownership or delete the group instead, so a group can
/// never end up ownerless. Leaving also revokes any group-admin ACL grant (idempotent) and closes the chat seat.
/// </summary>
internal sealed record LeaveGroupCommand(Ulid GroupId) : ICommand;

internal sealed class LeaveGroupCommandHandler : ICommandHandler<LeaveGroupCommand>
{
    private readonly IGroupRepository _groups;
    private readonly IConversationRepository _conversations;
    private readonly IAclGrantService _aclGrants;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public LeaveGroupCommandHandler(
        IGroupRepository groups,
        IConversationRepository conversations,
        IAclGrantService aclGrants,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _conversations = conversations;
        _aclGrants = aclGrants;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(LeaveGroupCommand command, CancellationToken cancellationToken)
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

        var membership = await _groups.GetMembershipAsync(group.Id, userId, cancellationToken);
        if (membership is null)
        {
            return Result.Failure(SocialErrors.MembershipNotFound);
        }

        if (group.OwnerId == userId)
        {
            return Result.Failure(GroupErrors.OwnerCannotLeave);
        }

        var now = _clock.GetUtcNow();
        await GroupMembershipWriter.RemoveMemberAsync(_groups, _conversations, membership, now, cancellationToken);
        _outbox.Enqueue(new GroupMemberLeftIntegrationEvent(
            Ulid.NewUlid(), group.Id, userId, Removed: false, RemovedBy: null, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // After the membership commit: an admin who left keeps no residue grant (idempotent when none existed).
        await _aclGrants.RevokeAsync(userId, Permissions.Moderate, PermissionScopes.Group, group.Id, cancellationToken);
        return Result.Success();
    }
}
