using Forum.Common.Security;
using Forum.Modules.Engagement.Application.Abstractions;

namespace Forum.Modules.Engagement.Application;

/// <summary>
/// The private-category visibility gate, mirroring Content's owner-vs-moderator shape: content in a private
/// category is visible (and reactable) only to the category owner or someone holding <c>moderate</c> at that
/// category's scope — which covers global moderators as well as per-category <c>acl_entries</c> grants.
/// </summary>
internal static class EngagementAuthorization
{
    public static async Task<bool> MaySeePrivateCategoryAsync(
        this ICurrentUser user, ReactionTarget target, CancellationToken cancellationToken) =>
        user.IsOwner(target.CategoryOwnerId)
        || await user.HasPermissionAsync(
            Permissions.Moderate, PermissionScopes.Category, target.CategoryId, cancellationToken);
}
