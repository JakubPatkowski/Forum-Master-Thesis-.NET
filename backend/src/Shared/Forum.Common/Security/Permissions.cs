namespace Forum.Common.Security;

/// <summary>
/// The action-code vocabulary that mirrors the <c>forum_authz.actions</c> bit catalog. Each code maps to one bit
/// in the permission mask; the resource type is implied by the <see cref="PermissionScopes">scope</see>, not the code.
/// </summary>
public static class Permissions
{
    /// <summary>View content.</summary>
    public const string Read = "read";

    /// <summary>Create a thread/category.</summary>
    public const string Create = "create";

    /// <summary>Edit content (ownership is enforced separately in the handler).</summary>
    public const string Update = "update";

    /// <summary>Delete content (ownership is enforced separately in the handler).</summary>
    public const string Delete = "delete";

    /// <summary>Post a comment.</summary>
    public const string Comment = "comment";

    /// <summary>React to content.</summary>
    public const string Like = "like";

    /// <summary>Act on content owned by anyone (moderator capability).</summary>
    public const string Moderate = "moderate";

    /// <summary>Administer users, roles and ACL entries (admin capability).</summary>
    public const string Manage = "manage";
}
