using System.Data;
using System.Data.Common;
using System.Globalization;

using Forum.Modules.Engagement.Application;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Application.Reactions;
using Forum.Modules.Engagement.Application.Stats;
using Forum.Modules.Engagement.Domain.Reactions;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Engagement.Infrastructure.Persistence;

/// <summary>
/// The Engagement read side, raw ADO like Content's view queries. Counts come from the trigger-maintained
/// <c>reaction_counts</c> table (primary-key lookups — the reactions table is never aggregated at read time);
/// stats come from the cross-schema <c>user_stats_v</c> view. Both live outside the EF model.
/// </summary>
internal sealed class EngagementQueries : IEngagementQueries
{
    private readonly EngagementDbContext _db;

    public EngagementQueries(EngagementDbContext db) => _db = db;

    public async Task<ReactionSummaryResponse> GetSummaryAsync(
        ReactionTargetType targetType, Ulid targetId, Ulid? viewerId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
              COALESCE((SELECT reaction_count FROM forum_engagement.reaction_counts
                         WHERE target_type = @targetType AND target_id = @targetId
                           AND reaction_type = @reactionType), 0) AS reaction_count,
              EXISTS (SELECT 1 FROM forum_engagement.reactions
                       WHERE user_id = @viewerId AND target_type = @targetType
                         AND target_id = @targetId AND reaction_type = @reactionType) AS viewer_reacted
            """;
        command.AddParameter("@targetType", ReactionTargets.ToWire(targetType), DbType.String);
        command.AddParameter("@targetId", targetId.ToString(), DbType.String);
        command.AddParameter("@reactionType", ReactionTypes.Like, DbType.String);
        command.AddParameter("@viewerId", viewerId?.ToString() ?? (object)DBNull.Value, DbType.String);

        var opened = await EnsureOpenAsync(connection, cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new ReactionSummaryResponse(targetId, reader.GetInt32(0), reader.GetBoolean(1));
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<ReactionSummaryResponse>> GetSummariesAsync(
        ReactionTargetType targetType, IReadOnlyList<Ulid> targetIds, Ulid? viewerId,
        CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();

        // Two indexed lookups in one round trip; missing counter rows read as zero on the compose below.
        command.CommandText =
            """
            SELECT target_id, reaction_count
            FROM forum_engagement.reaction_counts
            WHERE target_type = @targetType AND reaction_type = @reactionType AND target_id = ANY(@targetIds);

            SELECT target_id
            FROM forum_engagement.reactions
            WHERE user_id = @viewerId AND target_type = @targetType
              AND reaction_type = @reactionType AND target_id = ANY(@targetIds);
            """;
        command.AddParameter("@targetType", ReactionTargets.ToWire(targetType), DbType.String);
        command.AddParameter("@reactionType", ReactionTypes.Like, DbType.String);
        command.AddTextArrayParameter(
            "@targetIds", targetIds.Select(static id => id.ToString()).ToArray());
        command.AddParameter("@viewerId", viewerId?.ToString() ?? (object)DBNull.Value, DbType.String);

        var opened = await EnsureOpenAsync(connection, cancellationToken);
        try
        {
            var counts = new Dictionary<Ulid, int>(targetIds.Count);
            var viewerReacted = new HashSet<Ulid>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                counts[ReadUlid(reader, 0)] = reader.GetInt32(1);
            }

            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                viewerReacted.Add(ReadUlid(reader, 0));
            }

            return targetIds
                .Select(id => new ReactionSummaryResponse(
                    id, counts.GetValueOrDefault(id), viewerReacted.Contains(id)))
                .ToArray();
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<UserStatsResponse?> GetUserStatsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT user_id, username, display_name, thread_count, comment_count, karma
            FROM forum_engagement.user_stats_v
            WHERE user_id = @userId
            """;
        command.AddParameter("@userId", userId.ToString(), DbType.String);

        var opened = await EnsureOpenAsync(connection, cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new UserStatsResponse(
                ReadUlid(reader, 0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5));
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> EnsureOpenAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection.State == ConnectionState.Open)
        {
            return false;
        }

        await connection.OpenAsync(cancellationToken);
        return true;
    }

    private static Ulid ReadUlid(DbDataReader reader, int ordinal) =>
        Ulid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
}
