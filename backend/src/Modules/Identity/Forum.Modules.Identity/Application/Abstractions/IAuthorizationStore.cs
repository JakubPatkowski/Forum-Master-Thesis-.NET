namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>A role row from <c>forum_authz.roles</c>.</summary>
internal sealed record RoleInfo(Ulid RoleId, string Name, int AllowBits);

/// <summary>An ACL entry to add to <c>forum_authz.acl_entries</c> for a user principal.</summary>
internal sealed record AclEntryInput(string Scope, Ulid? ScopeId, int AllowBits, int DenyBits);

/// <summary>
/// Write/read access to the <c>forum_authz</c> RBAC + ACL tables (Identity owns this schema). Permission *resolution*
/// is the shared <see cref="Forum.Common.Security.IPermissionService"/>; this port covers administration and cache upkeep.
/// </summary>
internal interface IAuthorizationStore
{
    Task<RoleInfo?> GetRoleByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetRoleNamesForUserAsync(Ulid userId, CancellationToken cancellationToken);

    /// <summary>Idempotently grants a role to a user.</summary>
    Task AssignRoleAsync(Ulid userId, Ulid roleId, CancellationToken cancellationToken);

    /// <summary>Removes a role from a user (no-op if not assigned).</summary>
    Task RevokeRoleAsync(Ulid userId, Ulid roleId, CancellationToken cancellationToken);

    Task AddUserAclEntryAsync(Ulid userId, AclEntryInput entry, CancellationToken cancellationToken);

    /// <summary>Recomputes and upserts the user's <c>effective_perm_cache</c> row(s) after a role/ACL change.</summary>
    Task RecomputeUserCacheAsync(Ulid userId, CancellationToken cancellationToken);
}
