using Forum.Infrastructure.Seeding;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Domain.Users;
using Forum.Modules.Identity.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Npgsql;

namespace Forum.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Seeds <c>forum_identity</c> (users) and <c>forum_authz</c> (role grants + private-category ACLs). Runs first so
/// every downstream module can reference user ids. Writes directly through the DbContext with one reused Argon2id
/// hash and no domain/integration events, then warms the permission cache exactly as the register handler does.
/// </summary>
internal sealed class IdentitySeeder : IModuleSeeder
{
    private const int BatchSize = 1000;
    private const int ModerateBit = 1 << 6; // matches forum_authz.actions('moderate') — opens private categories.

    // The fixed role ids seeded by the AuthzSchema migration (user < moderator < admin).
    private const string UserRoleId = "00000000000000000000000001";
    private const string ModeratorRoleId = "00000000000000000000000002";
    private const string AdminRoleId = "00000000000000000000000003";

    // The Development cast, in role order (admin, moderator, then plain users).
    private static readonly string[] DevelopmentUsernames = ["admin", "mod", "alice", "bob", "charlie"];

    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(IdentityDbContext db, IPasswordHasher passwordHasher, ILogger<IdentitySeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public int Order => 1;

    public async Task SeedAsync(SeedConfig config, CancellationToken cancellationToken)
    {
        var plan = SeedPlan.For(config.Profile);

        if (config.AllowTruncate)
        {
            await TruncateAsync(cancellationToken);
        }
        else if (await _db.Users.AnyAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "forum_identity.users is not empty — refusing to seed a populated database. Re-run with --force to reset it.");
        }

        // ONE Argon2id hash for the whole profile: hashing hundreds of passwords individually would add minutes of
        // CPU (Argon2 is deliberately slow). The salt is random per run, so password_hash is not byte-reproducible
        // across runs — determinism is about ids/keysets, and login still verifies against the known plaintext.
        var passwordHash = _passwordHasher.Hash(plan.Password);

        var userIds = new string[plan.UserCount];
        var buffer = new List<User>(BatchSize);
        for (var index = 0; index < plan.UserCount; index++)
        {
            var id = SeedUlids.Create(SeedStreams.User, index);
            userIds[index] = id.ToString();

            var (username, email, displayName) = Identity(plan, index);
            var status = plan.IsBlocked(index) ? UserStatus.Blocked : UserStatus.Active;

            buffer.Add(User.Seed(id, username, email, displayName, passwordHash, status, SeedTime.At(SeedStreams.User, index)));
            if (buffer.Count >= BatchSize)
            {
                await FlushAsync(buffer, config, cancellationToken);
            }
        }

        await FlushAsync(buffer, config, cancellationToken);

        await AssignRolesAsync(plan, userIds, cancellationToken);
        await AddPrivateCategoryAclsAsync(plan, cancellationToken);
        await RecomputePermissionCacheAsync(userIds, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Identity seeded: {Users} users ({Admins} admin, {Moderators} moderator, {Blocked} blocked).",
                plan.UserCount, plan.AdminCount, plan.ModeratorCount, plan.BlockedCount);
        }
    }

    private static (string Username, string Email, string DisplayName) Identity(SeedPlan plan, int index)
    {
        if (plan.Profile == SeedProfile.Development)
        {
            var name = DevelopmentUsernames[index];
            var display = char.ToUpperInvariant(name[0]) + name[1..];
            return (name, $"{name}@dev.local", display);
        }

        var username = $"bench_user_{index + 1:D4}";
        return (username, $"{username}@bench.local", $"Bench User {index + 1:D4}");
    }

    private async Task FlushAsync(List<User> buffer, SeedConfig config, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        await _db.Users.AddRangeAsync(buffer, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        // Detach the batch so EF change-tracking stays O(batch), not O(total).
        _db.ChangeTracker.Clear();

        if (config.Verbose && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("  … {Count} users written.", buffer.Count);
        }

        buffer.Clear();
    }

    private async Task AssignRolesAsync(SeedPlan plan, string[] userIds, CancellationToken cancellationToken)
    {
        // Every account gets the baseline 'user' role; staff get their elevated role too.
        var users = new List<string>(userIds.Length + plan.StaffCount);
        var roles = new List<string>(userIds.Length + plan.StaffCount);
        for (var index = 0; index < userIds.Length; index++)
        {
            users.Add(userIds[index]);
            roles.Add(UserRoleId);

            if (plan.IsAdmin(index))
            {
                users.Add(userIds[index]);
                roles.Add(AdminRoleId);
            }
            else if (plan.IsModerator(index))
            {
                users.Add(userIds[index]);
                roles.Add(ModeratorRoleId);
            }
        }

        await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO forum_authz.user_roles (user_id, role_id)
            SELECT u, r FROM unnest(@users::text[], @roles::text[]) AS t(u, r)
            ON CONFLICT DO NOTHING
            """,
            [new NpgsqlParameter("users", users.ToArray()), new NpgsqlParameter("roles", roles.ToArray())],
            cancellationToken);
    }

    private async Task AddPrivateCategoryAclsAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        // Category ids are reconstructed from the shared deterministic convention (no Content reference); the ACL
        // rows live in forum_authz, this module's own schema. moderate at category scope is what the private-category
        // gate actually checks (CreateThread / attach / visibility), so members reach the private category.
        var aclIds = new List<string>();
        var scopeIds = new List<string>();
        var principalIds = new List<string>();

        var aclOrdinal = 0;
        for (var categoryIndex = 0; categoryIndex < plan.CategoryCount; categoryIndex++)
        {
            if (!plan.IsPrivateCategory(categoryIndex))
            {
                continue;
            }

            var categoryId = SeedUlids.Create(SeedStreams.Category, categoryIndex).ToString();
            foreach (var memberIndex in plan.PrivateCategoryMemberIndices(categoryIndex))
            {
                aclIds.Add(SeedUlids.Create("acl", aclOrdinal++).ToString());
                scopeIds.Add(categoryId);
                principalIds.Add(SeedUlids.Create(SeedStreams.User, memberIndex).ToString());
            }
        }

        if (aclIds.Count == 0)
        {
            return;
        }

        await _db.Database.ExecuteSqlRawAsync(
            $"""
            INSERT INTO forum_authz.acl_entries
                (acl_id, scope, scope_id, principal_type, principal_id, allow_bits, deny_bits)
            SELECT a, 'category', s, 'user', p, {ModerateBit}, 0
            FROM unnest(@aclIds::text[], @scopeIds::text[], @principalIds::text[]) AS t(a, s, p)
            """,
            [
                new NpgsqlParameter("aclIds", aclIds.ToArray()),
                new NpgsqlParameter("scopeIds", scopeIds.ToArray()),
                new NpgsqlParameter("principalIds", principalIds.ToArray()),
            ],
            cancellationToken);
    }

    private Task<int> RecomputePermissionCacheAsync(string[] userIds, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            "SELECT forum_authz.recompute_user_perms(u) FROM unnest(@users::text[]) AS u",
            [new NpgsqlParameter("users", userIds)],
            cancellationToken);

    private Task<int> TruncateAsync(CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE forum_identity.users, forum_identity.refresh_tokens,
                     forum_identity.outbox_messages, forum_identity.inbox_messages,
                     forum_authz.user_roles, forum_authz.acl_entries, forum_authz.effective_perm_cache
            RESTART IDENTITY CASCADE
            """,
            cancellationToken);
}
