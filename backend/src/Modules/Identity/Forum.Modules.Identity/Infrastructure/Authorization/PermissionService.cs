using Forum.Common.Security;
using Forum.Modules.Identity.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Identity.Infrastructure.Authorization;

/// <summary>
/// Resolves permissions by asking PostgreSQL the single question "what can this user do here?" via the
/// <c>forum_authz.effective_mask()</c> resolver and its <c>has_permission()</c> convenience wrapper (ADR 0004).
/// </summary>
internal sealed class PermissionService : IPermissionService
{
    private readonly IdentityDbContext _db;

    public PermissionService(IdentityDbContext db) => _db = db;

    public async Task<bool> HasPermissionAsync(
        Ulid userId, string action, string scope, Ulid? scopeId = null, CancellationToken cancellationToken = default)
    {
        var result = await _db.Database
            .SqlQueryRaw<bool>(
                "SELECT forum_authz.has_permission({0}, {1}, {2}, {3}) AS \"Value\"",
                userId.ToString(), action, scope, ScopeIdParameter(scopeId))
            .ToListAsync(cancellationToken);

        return result.Count > 0 && result[0];
    }

    public async Task<int> GetEffectiveMaskAsync(
        Ulid userId, string scope, Ulid? scopeId = null, CancellationToken cancellationToken = default)
    {
        var result = await _db.Database
            .SqlQueryRaw<int>(
                "SELECT forum_authz.effective_mask({0}, {1}, {2}) AS \"Value\"",
                userId.ToString(), scope, ScopeIdParameter(scopeId))
            .ToListAsync(cancellationToken);

        return result.Count > 0 ? result[0] : 0;
    }

    private static object ScopeIdParameter(Ulid? scopeId) => scopeId?.ToString() ?? (object)DBNull.Value;
}
