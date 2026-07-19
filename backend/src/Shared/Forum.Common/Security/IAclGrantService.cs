namespace Forum.Common.Security;

/// <summary>
/// Grants and revokes a single permission bit for a user at a scope, synchronously (ACL row + effective-perm
/// cache recompute in one call — the same "no async revocation window" stance Phase 6 took for role changes).
/// Identity owns the implementation (and the <c>forum_authz</c> schema it writes); other modules use it when a
/// domain action implies a permission change — e.g. Social promoting a group member to admin grants
/// <see cref="Permissions.Moderate"/> at <see cref="PermissionScopes.Group"/> scope. Sits next to
/// <see cref="IPermissionService"/>: that one resolves, this one administers.
/// </summary>
public interface IAclGrantService
{
    /// <summary>Idempotently ensures the user's allow mask at the scope contains the bit for <paramref name="action"/>.</summary>
    Task GrantAsync(Ulid userId, string action, string scope, Ulid? scopeId, CancellationToken cancellationToken);

    /// <summary>Removes the bit for <paramref name="action"/> from the user's allow mask at the scope (no-op when absent).</summary>
    Task RevokeAsync(Ulid userId, string action, string scope, Ulid? scopeId, CancellationToken cancellationToken);
}
