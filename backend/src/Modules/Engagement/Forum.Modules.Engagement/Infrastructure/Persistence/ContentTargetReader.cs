using System.Data;
using System.Globalization;

using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Domain.Reactions;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Engagement.Infrastructure.Persistence;

/// <summary>
/// Resolves reaction targets with a read-only cross-schema query into <c>forum_content</c> — the sanctioned
/// "later module read-joins an earlier module's tables" pattern (Content's own views do the same over
/// <c>forum_identity.users</c>; Engagement migrates last, so those tables exist). Never writes, never a DB FK.
/// Soft-deleted comments/threads/categories make the target unresolvable, so reacting to them 404s.
/// </summary>
internal sealed class ContentTargetReader : IReactionTargetReader
{
    private const string ThreadSql =
        """
        SELECT t.category_id, c.owner_id, c.visibility
        FROM forum_content.threads t
        JOIN forum_content.categories c ON c.id = t.category_id
        WHERE t.id = @targetId AND t.is_deleted = false AND c.is_deleted = false
        """;

    private const string CommentSql =
        """
        SELECT t.category_id, c.owner_id, c.visibility
        FROM forum_content.comments cm
        JOIN forum_content.threads t ON t.id = cm.thread_id
        JOIN forum_content.categories c ON c.id = t.category_id
        WHERE cm.id = @targetId AND cm.is_deleted = false AND t.is_deleted = false AND c.is_deleted = false
        """;

    private readonly EngagementDbContext _db;

    public ContentTargetReader(EngagementDbContext db) => _db = db;

    public async Task<ReactionTarget?> GetAsync(
        ReactionTargetType targetType, Ulid targetId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = targetType == ReactionTargetType.Thread ? ThreadSql : CommentSql;
        command.AddParameter("@targetId", targetId.ToString(), DbType.String);

        var opened = connection.State != ConnectionState.Open;
        if (opened)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ReactionTarget(
                Ulid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                Ulid.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                CategoryIsPrivate: reader.GetString(2) == "private");
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}
