using System.Data;
using System.Globalization;

using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Identity.Infrastructure.Authorization;

/// <summary>Administration writes against the <c>forum_authz</c> RBAC + ACL tables, plus permission-cache upkeep.</summary>
internal sealed class AuthorizationStore : IAuthorizationStore
{
    private readonly IdentityDbContext _db;

    public AuthorizationStore(IdentityDbContext db) => _db = db;

    public async Task<RoleInfo?> GetRoleByNameAsync(string name, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT role_id, name, allow_bits FROM forum_authz.roles WHERE name = @name LIMIT 1";
        command.AddParameter("@name", name);

        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new RoleInfo(
                Ulid.Parse(reader.GetString(0), CultureInfo.InvariantCulture), reader.GetString(1), reader.GetInt32(2));
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<string>> GetRoleNamesForUserAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var roles = await _db.Database
            .SqlQueryRaw<string>(
                """
                SELECT r.name AS "Value"
                FROM forum_authz.user_roles ur
                JOIN forum_authz.roles r ON r.role_id = ur.role_id
                WHERE ur.user_id = {0}
                ORDER BY r.name
                """,
                userId.ToString())
            .ToListAsync(cancellationToken);

        return roles;
    }

    public Task AssignRoleAsync(Ulid userId, Ulid roleId, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO forum_authz.user_roles (user_id, role_id) VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
            [userId.ToString(), roleId.ToString()],
            cancellationToken);

    public Task RevokeRoleAsync(Ulid userId, Ulid roleId, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM forum_authz.user_roles WHERE user_id = {0} AND role_id = {1}",
            [userId.ToString(), roleId.ToString()],
            cancellationToken);

    public Task AddUserAclEntryAsync(Ulid userId, AclEntryInput entry, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO forum_authz.acl_entries
                (acl_id, scope, scope_id, principal_type, principal_id, allow_bits, deny_bits)
            VALUES ({0}, {1}, {2}, 'user', {3}, {4}, {5})
            """,
            [
                Ulid.NewUlid().ToString(),
                entry.Scope,
                entry.ScopeId?.ToString() ?? (object)DBNull.Value,
                userId.ToString(),
                entry.AllowBits,
                entry.DenyBits,
            ],
            cancellationToken);

    public Task RecomputeUserCacheAsync(Ulid userId, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            "SELECT forum_authz.recompute_user_perms({0})",
            [userId.ToString()],
            cancellationToken);
}
