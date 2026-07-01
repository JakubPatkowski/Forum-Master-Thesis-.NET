using Forum.Infrastructure.Messaging;
using Forum.Modules.Identity.Infrastructure.Persistence;
using Forum.SharedKernel.Domain;
using Forum.TestUtilities;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using NpgsqlTypes;

using Shouldly;

using Xunit;

namespace Forum.Modules.Identity.Tests.Integration;

/// <summary>
/// Exercises the SQL bitmask ACL (ADR 0004) against a real Postgres: the <c>effective_mask()</c> resolver, the
/// <c>has_permission()</c> wrapper, allow/deny algebra and the cache recompute. Skipped when Docker is unavailable.
/// </summary>
[Collection("acl-sql")]
public sealed class AclSqlTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // Seeded role ids (mirrors the AddAuthzSchema migration).
    private const string UserRoleId = "00000000000000000000000001";
    private const string ModeratorRoleId = "00000000000000000000000002";

    // Bit positions from the action catalog.
    private const int Like = 1 << 5;
    private const int Moderate = 1 << 6;
    private const int UserRoleMask = 63;       // read|create|update|delete|comment|like
    private const int ModeratorRoleMask = 127; // + moderate

    private readonly PostgresFixture _fixture;

    public AclSqlTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        if (!_fixture.Available)
        {
            return;
        }

        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [SkippableFact]
    public async Task Role_template_drives_the_global_effective_mask()
    {
        Skip.IfNot(_fixture.Available, "Docker is not available.");

        await using var db = CreateContext();
        var user = Ulid.NewUlid().ToString();
        await AssignRole(db, user, UserRoleId);

        (await EffectiveMask(db, user, "global", null)).ShouldBe(UserRoleMask);
        (await HasPermission(db, user, "create", "global", null)).ShouldBeTrue();
        (await HasPermission(db, user, "moderate", "global", null)).ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Moderator_role_adds_the_moderate_bit()
    {
        Skip.IfNot(_fixture.Available, "Docker is not available.");

        await using var db = CreateContext();
        var user = Ulid.NewUlid().ToString();
        await AssignRole(db, user, ModeratorRoleId);

        (await EffectiveMask(db, user, "global", null)).ShouldBe(ModeratorRoleMask);
        (await HasPermission(db, user, "moderate", "global", null)).ShouldBeTrue();
    }

    [SkippableFact]
    public async Task A_category_scoped_acl_grant_acts_as_a_per_context_role()
    {
        Skip.IfNot(_fixture.Available, "Docker is not available.");

        await using var db = CreateContext();
        var user = Ulid.NewUlid().ToString();
        var category = Ulid.NewUlid().ToString();
        var otherCategory = Ulid.NewUlid().ToString();
        await AssignRole(db, user, UserRoleId);
        await AddAcl(db, user, "category", category, allowBits: Moderate, denyBits: 0);

        // In the granted category the user is effectively a moderator; elsewhere they are just a user.
        (await HasPermission(db, user, "moderate", "category", category)).ShouldBeTrue();
        (await HasPermission(db, user, "moderate", "category", otherCategory)).ShouldBeFalse();
        (await EffectiveMask(db, user, "category", category)).ShouldBe(UserRoleMask | Moderate);
    }

    [SkippableFact]
    public async Task A_deny_entry_removes_a_role_granted_bit()
    {
        Skip.IfNot(_fixture.Available, "Docker is not available.");

        await using var db = CreateContext();
        var user = Ulid.NewUlid().ToString();
        await AssignRole(db, user, UserRoleId);
        await AddAcl(db, user, "global", scopeId: null, allowBits: 0, denyBits: Like);

        (await EffectiveMask(db, user, "global", null)).ShouldBe(UserRoleMask & ~Like);
        (await HasPermission(db, user, "like", "global", null)).ShouldBeFalse();
        (await HasPermission(db, user, "create", "global", null)).ShouldBeTrue();
    }

    [SkippableFact]
    public async Task Recompute_materializes_the_effective_cache()
    {
        Skip.IfNot(_fixture.Available, "Docker is not available.");

        await using var db = CreateContext();
        var user = Ulid.NewUlid().ToString();
        var category = Ulid.NewUlid().ToString();
        await AssignRole(db, user, UserRoleId);
        await AddAcl(db, user, "category", category, allowBits: Moderate, denyBits: 0);

        await db.Database.ExecuteSqlRawAsync("SELECT forum_authz.recompute_user_perms({0})", user);

        var global = await CachedEffective(db, user, "global");
        var scoped = await CachedEffective(db, user, "category");
        global.ShouldBe(UserRoleMask);
        scoped.ShouldBe(UserRoleMask | Moderate);
    }

    private static async Task AssignRole(IdentityDbContext db, string userId, string roleId) =>
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO forum_authz.user_roles (user_id, role_id) VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
            userId, roleId);

    private static async Task AddAcl(IdentityDbContext db, string userId, string scope, string? scopeId, int allowBits, int denyBits) =>
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO forum_authz.acl_entries
                (acl_id, scope, scope_id, principal_type, principal_id, allow_bits, deny_bits)
            VALUES (@acl_id, @scope, @scope_id, 'user', @principal_id, @allow_bits, @deny_bits)
            """,
            new NpgsqlParameter("acl_id", Ulid.NewUlid().ToString()),
            new NpgsqlParameter("scope", scope),
            NullableText("scope_id", scopeId),
            new NpgsqlParameter("principal_id", userId),
            new NpgsqlParameter("allow_bits", allowBits),
            new NpgsqlParameter("deny_bits", denyBits));

    private static async Task<int> EffectiveMask(IdentityDbContext db, string userId, string scope, string? scopeId)
    {
        var rows = await db.Database
            .SqlQueryRaw<int>(
                "SELECT forum_authz.effective_mask(@user_id, @scope, @scope_id) AS \"Value\"",
                new NpgsqlParameter("user_id", userId),
                new NpgsqlParameter("scope", scope),
                NullableText("scope_id", scopeId))
            .ToListAsync();
        return rows[0];
    }

    // A bare DBNull.Value has no store-type mapping; pin the type so a NULL scope_id resolves.
    private static NpgsqlParameter NullableText(string name, string? value) =>
        new(name, NpgsqlDbType.Text) { Value = value ?? (object)DBNull.Value };

    private static async Task<bool> HasPermission(IdentityDbContext db, string userId, string action, string scope, string? scopeId)
    {
        var rows = await db.Database
            .SqlQueryRaw<bool>(
                "SELECT forum_authz.has_permission(@user_id, @action, @scope, @scope_id) AS \"Value\"",
                new NpgsqlParameter("user_id", userId),
                new NpgsqlParameter("action", action),
                new NpgsqlParameter("scope", scope),
                NullableText("scope_id", scopeId))
            .ToListAsync();
        return rows[0];
    }

    private static async Task<int> CachedEffective(IdentityDbContext db, string userId, string scope)
    {
        var rows = await db.Database
            .SqlQueryRaw<int>(
                "SELECT effective AS \"Value\" FROM forum_authz.effective_perm_cache WHERE user_id = {0} AND scope = {1}",
                userId, scope)
            .ToListAsync();
        return rows[0];
    }

    private IdentityDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new IdentityDbContext(options, new NoOpDispatcher());
    }

    private sealed class NoOpDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
