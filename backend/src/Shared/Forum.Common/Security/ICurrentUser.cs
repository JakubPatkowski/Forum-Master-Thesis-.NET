namespace Forum.Common.Security;

/// <summary>
/// The authenticated principal for the current request: identity (from the JWT) plus ownership and permission
/// helpers (resolved against the SQL ACL). Registered scoped; also backs <see cref="ICurrentActor"/> so the audit
/// interceptor stamps the real user. Null/empty when the request is anonymous.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The user ULID from the <c>sub</c> claim, or null when anonymous.</summary>
    Ulid? Id { get; }

    /// <summary>True when the request carries a valid authenticated identity.</summary>
    bool IsAuthenticated { get; }

    /// <summary>The global role names carried in the token (<c>user</c>/<c>moderator</c>/<c>admin</c>).</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>True when the current user owns the resource (their id equals <paramref name="ownerId"/>).</summary>
    bool IsOwner(Ulid ownerId);

    /// <summary>Resolves a permission for the current user against the SQL ACL at the given scope.</summary>
    Task<bool> HasPermissionAsync(
        string action, string scope = PermissionScopes.Global, Ulid? scopeId = null, CancellationToken cancellationToken = default);
}
