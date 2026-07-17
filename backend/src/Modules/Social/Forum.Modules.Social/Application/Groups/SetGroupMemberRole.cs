using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Promotes/demotes a member (manage-gate). "admin" grants <c>moderate</c> at the group's ACL scope, "member"
/// revokes it — the grant IS the role, no role column exists (permissions-acl-design's group scope made real).
/// The owner is above roles (422). Synchronous grant + cache recompute: the promotion is effective on the very
/// next request, and a demotion cannot linger.
/// </summary>
internal sealed record SetGroupMemberRoleCommand(Ulid GroupId, Ulid UserId, string Role) : ICommand;

internal sealed class SetGroupMemberRoleCommandHandler : ICommandHandler<SetGroupMemberRoleCommand>
{
    private readonly IGroupRepository _groups;
    private readonly IAclGrantService _aclGrants;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public SetGroupMemberRoleCommandHandler(
        IGroupRepository groups,
        IAclGrantService aclGrants,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _aclGrants = aclGrants;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(SetGroupMemberRoleCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is null)
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

        if (await _groups.GetMembershipAsync(group.Id, command.UserId, cancellationToken) is null)
        {
            return Result.Failure(SocialErrors.MembershipNotFound);
        }

        if (group.OwnerId == command.UserId)
        {
            return Result.Failure(SocialErrors.OwnerRoleImmutable);
        }

        var role = command.Role?.Trim().ToLowerInvariant();
        if (role is not (GroupWire.AdminRole or GroupWire.MemberRole))
        {
            return Result.Failure(SocialErrors.UnknownRole);
        }

        if (role == GroupWire.AdminRole)
        {
            await _aclGrants.GrantAsync(
                command.UserId, Permissions.Moderate, PermissionScopes.Group, group.Id, cancellationToken);
        }
        else
        {
            await _aclGrants.RevokeAsync(
                command.UserId, Permissions.Moderate, PermissionScopes.Group, group.Id, cancellationToken);
        }

        // Member-list views read is_admin straight from the ACL, so a group refresh event is all clients need.
        _outbox.Enqueue(new GroupUpdatedIntegrationEvent(Ulid.NewUlid(), group.Id, _clock.GetUtcNow()));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
