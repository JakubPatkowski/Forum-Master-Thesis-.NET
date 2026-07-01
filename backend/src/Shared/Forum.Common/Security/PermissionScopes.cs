namespace Forum.Common.Security;

/// <summary>
/// The scopes a permission can be evaluated at. A grant from a role template applies at every scope; an
/// <c>acl_entries</c> row narrows or widens it for one object (e.g. a category moderator).
/// </summary>
public static class PermissionScopes
{
    /// <summary>Site-wide scope (role templates, admin capabilities). <c>scope_id</c> is null.</summary>
    public const string Global = "global";

    /// <summary>A single category; <c>scope_id</c> is the category ULID.</summary>
    public const string Category = "category";

    /// <summary>A single thread; <c>scope_id</c> is the thread ULID.</summary>
    public const string Thread = "thread";
}
