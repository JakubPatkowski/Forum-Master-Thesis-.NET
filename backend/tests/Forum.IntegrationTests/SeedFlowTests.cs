using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using Forum.Infrastructure.Seeding;
using Forum.Infrastructure.Startup;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// End-to-end coverage of the Phase 9b Development seed against the real host + Postgres: exact row counts,
/// role/ACL wiring, trigger-maintained FTS and reaction counters, a real login on the reused Argon2id hash, the
/// idempotency guard, and byte-for-byte determinism across two fresh runs. The Benchmark profile gets ONE run of
/// its own in <see cref="SeedFlowBenchmarkTests"/> (row counts only). Skipped when Docker is unavailable.
/// </summary>
public sealed class SeedFlowTests : IClassFixture<ForumApiFactory>
{
    private const int ModerateBit = 1 << 6;

    private readonly ForumApiFactory _factory;

    public SeedFlowTests(ForumApiFactory factory) => _factory = factory;

    [SkippableFact]
    public async Task Development_seed_is_complete_consistent_and_deterministic()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        // One connection for all raw-SQL assertions — every module shares one database, so any DbContext's
        // connection reaches every forum_* schema.
        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetServices<DbContext>().First().Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        async Task<int> Scalar(string sql)
        {
            await using var command = Command(connection, sql);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        async Task<IReadOnlyList<string>> Ids(string sql)
        {
            await using var command = Command(connection, sql);
            await using var reader = await command.ExecuteReaderAsync();
            var ids = new List<string>();
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetString(0));
            }

            return ids;
        }

        // First run against the freshly-migrated (empty) database — the idempotency guard permits it.
        await SeedRunner.SeedAsync(_factory.Services, new SeedConfig(SeedProfile.Development));

        // --- Exact row counts (the Development profile table) --------------------------------------------------
        (await Scalar("SELECT count(*) FROM forum_identity.users")).ShouldBe(5);
        (await Scalar("SELECT count(*) FROM forum_content.categories")).ShouldBe(2);
        (await Scalar("SELECT count(*) FROM forum_content.threads")).ShouldBe(10);
        (await Scalar("SELECT count(*) FROM forum_content.comments")).ShouldBe(10);
        (await Scalar("SELECT count(*) FROM forum_engagement.reactions")).ShouldBeInRange(1, 5);

        // No outbox rows: seeding never raises integration events (the whole point vs a broker meltdown).
        (await Scalar("SELECT count(*) FROM forum_identity.outbox_messages")).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM forum_content.outbox_messages")).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM forum_engagement.outbox_messages")).ShouldBe(0);

        // --- Roles: everyone 'user', one admin, one moderator -------------------------------------------------
        (await Scalar("SELECT count(*) FROM forum_authz.user_roles WHERE role_id = '00000000000000000000000001'")).ShouldBe(5);
        (await Scalar("SELECT count(*) FROM forum_authz.user_roles WHERE role_id = '00000000000000000000000003'")).ShouldBe(1);
        (await Scalar("SELECT count(*) FROM forum_authz.user_roles WHERE role_id = '00000000000000000000000002'")).ShouldBe(1);

        // --- One private category, with its member ACL granting moderate at that category scope ----------------
        (await Scalar("SELECT count(*) FROM forum_content.categories WHERE visibility = 'private'")).ShouldBe(1);
        (await Scalar(
            $"SELECT count(*) FROM forum_authz.acl_entries WHERE scope = 'category' AND allow_bits = {ModerateBit}"))
            .ShouldBe(1);

        // --- FTS: the trigger filled search_tsv on every insert; the guaranteed 'seeded' token hits every thread.
        (await Scalar("SELECT count(*) FROM forum_content.threads WHERE search_tsv @@ websearch_to_tsquery('simple', 'seeded')"))
            .ShouldBe(10);

        // --- reaction_counts is trigger-maintained and never drifts from the reactions table -------------------
        (await Scalar("SELECT count(*) FROM forum_engagement.reaction_counts")).ShouldBeGreaterThan(0);
        (await Scalar(
            """
            SELECT count(*) FROM forum_engagement.reaction_counts rc
            WHERE rc.reaction_count <> (
                SELECT count(*) FROM forum_engagement.reactions r
                WHERE r.target_type = rc.target_type AND r.target_id = rc.target_id AND r.reaction_type = rc.reaction_type)
            """))
            .ShouldBe(0);

        // --- The reused Argon2id hash verifies: the admin can actually log in ----------------------------------
        var login = await _factory.CreateClient().PostAsJsonAsync(
            "/api/identity/login", new { email = "admin@dev.local", password = "Dev#Password1" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);

        // --- Determinism: capture ids, force-reseed a fresh dataset, capture again — must be identical ---------
        var firstUsers = await Ids("SELECT id FROM forum_identity.users ORDER BY id");
        var firstThreads = await Ids("SELECT id FROM forum_content.threads ORDER BY id");
        var firstComments = await Ids("SELECT id FROM forum_content.comments ORDER BY id");

        await SeedRunner.SeedAsync(_factory.Services, new SeedConfig(SeedProfile.Development, AllowTruncate: true));

        (await Ids("SELECT id FROM forum_identity.users ORDER BY id")).ShouldBe(firstUsers);
        (await Ids("SELECT id FROM forum_content.threads ORDER BY id")).ShouldBe(firstThreads);
        (await Ids("SELECT id FROM forum_content.comments ORDER BY id")).ShouldBe(firstComments);

        // --- Idempotency guard: seeding the now-populated database without --force aborts ---------------------
        await Should.ThrowAsync<InvalidOperationException>(
            SeedRunner.SeedAsync(_factory.Services, new SeedConfig(SeedProfile.Development)));
    }

    internal static DbCommand Command(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}

/// <summary>
/// The Benchmark profile end to end, ONE run on a fresh database. It lives in its own class (= its own factory and
/// Postgres) because <see cref="SeedFlowTests"/> needs an EMPTY database to exercise the idempotency guard, so the
/// two profiles must never share a container. Asserts the locked content counts and the Architecture-B-symmetric
/// social graph (A/B deviation D6): B's ring rule as row counts, the seat/friendship invariants the J9 direct-message
/// journey relies on, the pending-request bell rows and the read markers as <c>conversation_list_v</c> sees them.
/// Determinism is NOT re-proven here — the Development test covers the mechanism, and it is identical.
/// </summary>
public sealed class SeedFlowBenchmarkTests : IClassFixture<ForumApiFactory>
{
    private readonly ForumApiFactory _factory;

    public SeedFlowBenchmarkTests(ForumApiFactory factory) => _factory = factory;

    [SkippableFact]
    public async Task Benchmark_seed_mirrors_architecture_B_social_graph()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        using var scope = _factory.Services.CreateScope();
        var connection = scope.ServiceProvider.GetServices<DbContext>().First().Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        async Task<int> Scalar(string sql)
        {
            await using var command = SeedFlowTests.Command(connection, sql);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        // `--force` shape, exactly what `make seed ARGS=--benchmark` and the k8s Job run.
        await SeedRunner.SeedAsync(_factory.Services, new SeedConfig(SeedProfile.Benchmark, AllowTruncate: true));
        var plan = SeedPlan.For(SeedProfile.Benchmark);

        // --- The locked content numbers (proves the Benchmark profile ran, not Development) ---------------------
        (await Scalar("SELECT count(*) FROM forum_identity.users")).ShouldBe(800);
        (await Scalar("SELECT count(*) FROM forum_content.categories")).ShouldBe(12);
        (await Scalar("SELECT count(*) FROM forum_content.threads")).ShouldBe(1600);
        (await Scalar("SELECT count(*) FROM forum_content.comments")).ShouldBe(9000);
        (await Scalar("SELECT count(*) FROM forum_engagement.reactions")).ShouldBe(15000);

        // --- Friendships: 2 400 accepted + 80 pending, one row per unordered pair --------------------------------
        (await Scalar("SELECT count(*) FROM forum_social.friendships WHERE status = 'accepted'")).ShouldBe(plan.FriendshipCount);
        (await Scalar("SELECT count(*) FROM forum_social.friendships WHERE status = 'pending'")).ShouldBe(plan.PendingFriendshipCount);
        (await Scalar("SELECT count(*) FROM forum_social.friendships WHERE status = 'accepted' AND accepted_on_utc IS NULL")).ShouldBe(0);
        (await Scalar(
            """
            SELECT count(DISTINCT (LEAST(requester_id, addressee_id), GREATEST(requester_id, addressee_id)))
            FROM forum_social.friendships
            """))
            .ShouldBe(plan.FriendshipCount + plan.PendingFriendshipCount);

        // Every user has exactly six accepted friends (two per chord of the ring).
        (await Scalar(
            """
            SELECT count(*) FROM (
                SELECT u, count(*) AS degree FROM (
                    SELECT requester_id AS u FROM forum_social.friendships WHERE status = 'accepted'
                    UNION ALL
                    SELECT addressee_id FROM forum_social.friendships WHERE status = 'accepted') edges
                GROUP BY u) degrees
            WHERE degree <> 6
            """))
            .ShouldBe(0);

        // --- Pending requests: one unread friend.request bell row each, addressed to the addressee ---------------
        (await Scalar("SELECT count(*) FROM forum_social.notifications")).ShouldBe(plan.PendingFriendshipCount);
        (await Scalar(
            """
            SELECT count(*) FROM forum_social.notifications n
            JOIN forum_social.friendships f ON f.id = n.target_id AND f.status = 'pending'
            WHERE n.kind = 'friend.request' AND n.is_read = false
              AND n.user_id = f.addressee_id AND n.actor_id = f.requester_id
            """))
            .ShouldBe(plan.PendingFriendshipCount);

        // --- No groups at all (Architecture B parity) ----------------------------------------------------------
        (await Scalar("SELECT count(*) FROM forum_social.groups")).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM forum_social.group_memberships")).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM forum_social.group_invites")).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM forum_social.conversations WHERE type = 'group'")).ShouldBe(0);

        // --- Direct conversations: 800 (every ring pair), two active seats each, both seats accepted friends -----
        (await Scalar("SELECT count(*) FROM forum_social.conversations WHERE type = 'direct'")).ShouldBe(plan.DirectConversationCount);
        (await Scalar("SELECT count(*) FROM forum_social.conversation_participants WHERE left_on_utc IS NULL"))
            .ShouldBe(2 * plan.DirectConversationCount);
        (await Scalar(
            """
            SELECT count(*) FROM (
                SELECT conversation_id FROM forum_social.conversation_participants
                GROUP BY conversation_id HAVING count(*) <> 2) odd
            """))
            .ShouldBe(0);
        (await Scalar(
            """
            SELECT count(*) FROM forum_social.conversations c
            JOIN forum_social.conversation_participants a ON a.conversation_id = c.id
            JOIN forum_social.conversation_participants b ON b.conversation_id = c.id AND a.user_id < b.user_id
            WHERE c.type = 'direct' AND NOT EXISTS (
                SELECT 1 FROM forum_social.friendships f
                WHERE f.status = 'accepted'
                  AND LEAST(f.requester_id, f.addressee_id) = a.user_id
                  AND GREATEST(f.requester_id, f.addressee_id) = b.user_id)
            """))
            .ShouldBe(0);

        // --- Messages: 2 000 live rows, every sender holds a seat in its conversation (THE message-auth fact) ----
        (await Scalar("SELECT count(*) FROM forum_social.messages")).ShouldBe(plan.MessageCount);
        (await Scalar("SELECT count(*) FROM forum_social.messages WHERE is_deleted = true")).ShouldBe(0);
        (await Scalar(
            """
            SELECT count(*) FROM forum_social.messages m
            JOIN forum_social.conversation_participants p
              ON p.conversation_id = m.conversation_id AND p.user_id = m.owner_id
            """))
            .ShouldBe(plan.MessageCount);
        (await Scalar("SELECT count(DISTINCT conversation_id) FROM forum_social.messages")).ShouldBe(plan.DirectConversationCount);

        // --- Read markers: every fourth ring pair is read by users[p] (200 seats). Through conversation_list_v a
        //     marked seat shows nothing unread, and every message is unread for exactly one seat (its recipient).
        (await Scalar("SELECT count(*) FROM forum_social.conversation_participants WHERE last_read_on_utc IS NOT NULL"))
            .ShouldBe(200);
        (await Scalar(
            """
            SELECT count(*) FROM forum_social.conversation_list_v v
            JOIN forum_social.conversation_participants p
              ON p.conversation_id = v.conversation_id AND p.user_id = v.user_id
            WHERE p.last_read_on_utc IS NOT NULL AND v.unread_count > 0
            """))
            .ShouldBe(0);
        (await Scalar("SELECT coalesce(sum(unread_count), 0) FROM forum_social.conversation_list_v")).ShouldBe(plan.MessageCount);

        // --- Seeding never raises integration events — the social outbox stays empty like every other module's ---
        (await Scalar("SELECT count(*) FROM forum_social.outbox_messages")).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM forum_identity.outbox_messages")).ShouldBe(0);
        (await Scalar("SELECT count(*) FROM forum_content.outbox_messages")).ShouldBe(0);
    }
}
