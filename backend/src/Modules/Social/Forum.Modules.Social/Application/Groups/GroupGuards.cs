using Forum.Common.Security;
using Forum.Modules.Social.Domain.Groups;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// The group-management gate every admin-level handler shares: the owner, or anyone holding <c>moderate</c> at the
/// group's ACL scope (group admins get a per-group grant; global moderators/admins hold it via their role
/// template — deliberate: platform staff can act on reported groups, though membership-gated chat stays closed
/// to them).
/// </summary>
internal static class GroupGuards
{
    public static async Task<bool> MayManageAsync(
        ICurrentUser currentUser, Group group, CancellationToken cancellationToken) =>
        currentUser.IsOwner(group.OwnerId)
        || await currentUser.HasPermissionAsync(
            Permissions.Moderate, PermissionScopes.Group, group.Id, cancellationToken);
}
