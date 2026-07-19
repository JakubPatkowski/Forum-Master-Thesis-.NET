using Forum.Common.Security;
using Forum.Modules.Identity.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

namespace Forum.Modules.Identity.Infrastructure.Authorization;

/// <summary>
/// The shared <see cref="IAclGrantService"/> surface other modules use when a domain action implies a permission
/// change (Social's group-admin promotion grants <c>moderate</c> at <c>group</c> scope). <c>acl_entries</c> has no
/// unique key per (scope, principal), so grant is update-else-insert on the user's single allow row at the scope
/// (a concurrent double insert is harmless — <c>effective_mask()</c> ORs all rows); revoke clears the bit and
/// removes rows left with no bits. Both recompute the effective-perm cache synchronously — same "no async
/// revocation window" stance as the Phase 1 admin endpoints.
/// </summary>
internal sealed class AclGrantService : IAclGrantService
{
    private readonly IdentityDbContext _db;

    public AclGrantService(IdentityDbContext db) => _db = db;

    public async Task GrantAsync(Ulid userId, string action, string scope, Ulid? scopeId, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """
            WITH bit AS (SELECT (1 << bit) AS b FROM forum_authz.actions WHERE code = @action),
            updated AS (
                UPDATE forum_authz.acl_entries
                   SET allow_bits = allow_bits | (SELECT b FROM bit)
                 WHERE scope = @scope AND COALESCE(scope_id, '') = COALESCE(@scope_id, '')
                   AND principal_type = 'user' AND principal_id = @principal_id
                RETURNING acl_id)
            INSERT INTO forum_authz.acl_entries
                (acl_id, scope, scope_id, principal_type, principal_id, allow_bits, deny_bits)
            SELECT @acl_id, @scope, @scope_id, 'user', @principal_id, (SELECT b FROM bit), 0
            WHERE NOT EXISTS (SELECT 1 FROM updated)
            """,
            [
                new NpgsqlParameter("action", action),
                new NpgsqlParameter("scope", scope),
                new NpgsqlParameter("scope_id", NpgsqlDbType.Text) { Value = scopeId?.ToString() ?? (object)DBNull.Value },
                new NpgsqlParameter("principal_id", userId.ToString()),
                new NpgsqlParameter("acl_id", Ulid.NewUlid().ToString()),
            ],
            cancellationToken);

        await RecomputeAsync(userId, cancellationToken);
    }

    public async Task RevokeAsync(Ulid userId, string action, string scope, Ulid? scopeId, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlRawAsync(
            """
            WITH bit AS (SELECT (1 << bit) AS b FROM forum_authz.actions WHERE code = @action),
            cleared AS (
                UPDATE forum_authz.acl_entries
                   SET allow_bits = allow_bits & ~(SELECT b FROM bit)
                 WHERE scope = @scope AND COALESCE(scope_id, '') = COALESCE(@scope_id, '')
                   AND principal_type = 'user' AND principal_id = @principal_id
                RETURNING acl_id, allow_bits, deny_bits)
            DELETE FROM forum_authz.acl_entries e
             USING cleared c
             WHERE e.acl_id = c.acl_id AND c.allow_bits = 0 AND c.deny_bits = 0
            """,
            [
                new NpgsqlParameter("action", action),
                new NpgsqlParameter("scope", scope),
                new NpgsqlParameter("scope_id", NpgsqlDbType.Text) { Value = scopeId?.ToString() ?? (object)DBNull.Value },
                new NpgsqlParameter("principal_id", userId.ToString()),
            ],
            cancellationToken);

        await RecomputeAsync(userId, cancellationToken);
    }

    private Task<int> RecomputeAsync(Ulid userId, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            "SELECT forum_authz.recompute_user_perms({0})", [userId.ToString()], cancellationToken);
}
