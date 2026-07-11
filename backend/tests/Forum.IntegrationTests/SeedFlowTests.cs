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
/// idempotency guard, and byte-for-byte determinism across two fresh runs. The Benchmark profile is validated
/// manually (`make seed ARGS=--benchmark`) — too heavy for the routine suite. Skipped when Docker is unavailable.
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

    private static DbCommand Command(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
