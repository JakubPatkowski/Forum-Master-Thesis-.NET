using Forum.Common.Security;

namespace Forum.Modules.Content.Application;

/// <summary>
/// The author-vs-moderator gate shared by every Content write: the owner may act on their own content, anyone
/// else needs <c>moderate</c> resolved at the category scope — which covers global moderators (role template)
/// as well as per-category moderators (an <c>acl_entries</c> row at <c>scope='category'</c>).
/// </summary>
internal static class ContentAuthorization
{
    public static async Task<bool> IsOwnerOrModeratorAsync(
        this ICurrentUser user, Ulid ownerId, Ulid categoryId, CancellationToken cancellationToken) =>
        user.IsOwner(ownerId) || await user.IsModeratorOfAsync(categoryId, cancellationToken);

    public static Task<bool> IsModeratorOfAsync(
        this ICurrentUser user, Ulid categoryId, CancellationToken cancellationToken) =>
        user.HasPermissionAsync(Permissions.Moderate, PermissionScopes.Category, categoryId, cancellationToken);
}
