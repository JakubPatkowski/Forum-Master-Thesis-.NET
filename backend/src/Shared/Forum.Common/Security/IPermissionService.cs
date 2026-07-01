namespace Forum.Common.Security;

/// <summary>
/// Resolves "what can this user do here?" against the SQL bitmask ACL (the <c>effective_mask()</c> resolver).
/// The abstraction lives in the shared kernel so every module can gate on it; the Identity module owns the
/// implementation (and the <c>forum_authz</c> schema it reads).
/// </summary>
public interface IPermissionService
{
    /// <summary>True when the user's effective mask at the scope has the bit for <paramref name="action"/> set.</summary>
    Task<bool> HasPermissionAsync(
        Ulid userId, string action, string scope, Ulid? scopeId = null, CancellationToken cancellationToken = default);

    /// <summary>The raw effective bitmask (<c>allow &amp; ~deny</c>) for the user at the scope.</summary>
    Task<int> GetEffectiveMaskAsync(
        Ulid userId, string scope, Ulid? scopeId = null, CancellationToken cancellationToken = default);
}
