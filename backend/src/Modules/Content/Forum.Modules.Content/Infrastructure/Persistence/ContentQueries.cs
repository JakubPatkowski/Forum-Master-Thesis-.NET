using System.Data;
using System.Data.Common;
using System.Globalization;

using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Categories;
using Forum.Modules.Content.Application.Comments;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Tags;
using Forum.Modules.Content.Application.Threads;
using Forum.Modules.Content.Domain.Categories;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// The Content read side. Cross-schema reads (author display names) go through the SQL views via raw ADO;
/// single-module lookups use EF (no-tracking by default). Lists are keyset-paged — never OFFSET.
/// </summary>
internal sealed class ContentQueries : IContentQueries
{
    private const string FeedColumns =
        """
        id, category_id, category_slug, category_name, title, is_pinned,
        owner_id, username, display_name, like_count, comment_count,
        created_on_utc, last_modified_on_utc
        """;

    private readonly ContentDbContext _db;

    public ContentQueries(ContentDbContext db) => _db = db;

    public async Task<IReadOnlyList<CategoryResponse>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        var categories = await _db.Categories.OrderBy(static category => category.Name).ToListAsync(cancellationToken);
        return categories.Select(MapCategory).ToArray();
    }

    public async Task<CategoryResponse?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(
            category => category.Slug == slug, cancellationToken);
        return category is null ? null : MapCategory(category);
    }

    public Task<bool> CategoryExistsAsync(Ulid categoryId, CancellationToken cancellationToken) =>
        _db.Categories.AnyAsync(category => category.Id == categoryId, cancellationToken);

    public Task<bool> ThreadExistsAsync(Ulid threadId, CancellationToken cancellationToken) =>
        _db.Threads.AnyAsync(thread => thread.Id == threadId, cancellationToken);

    public async Task<CursorPage<ThreadFeedItemResponse>> GetThreadFeedAsync(
        Ulid categoryId, ThreadFeedCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT {FeedColumns}
            FROM forum_content.thread_feed_v
            WHERE category_id = @categoryId
              AND (@cursorId IS NULL
                   OR is_pinned < @cursorPinned
                   OR (is_pinned = @cursorPinned AND created_on_utc < @cursorCreated)
                   OR (is_pinned = @cursorPinned AND created_on_utc = @cursorCreated AND id < @cursorId))
            ORDER BY is_pinned DESC, created_on_utc DESC, id DESC
            LIMIT @limit
            """;
        command.AddParameter("@categoryId", categoryId.ToString(), DbType.String);
        command.AddParameter("@cursorId", cursor?.Id.ToString() ?? (object)DBNull.Value, DbType.String);
        command.AddParameter("@cursorPinned", cursor?.IsPinned ?? (object)DBNull.Value, DbType.Boolean);
        command.AddParameter("@cursorCreated", cursor?.CreatedOnUtc ?? (object)DBNull.Value, DbType.DateTimeOffset);
        command.AddParameter("@limit", limit + 1);

        var items = await ReadFeedRowsAsync(connection, command, limit, cancellationToken);
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var last = items.Count > 0 ? items[^1] : null;
        var nextCursor = hasMore && last is not null
            ? new ThreadFeedCursor(last.IsPinned, last.CreatedOnUtc, last.Id).Encode()
            : null;

        return new CursorPage<ThreadFeedItemResponse>(items, nextCursor, hasMore);
    }

    public async Task<CursorPage<ThreadFeedItemResponse>> SearchThreadsAsync(
        string query, ThreadSearchCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();

        // websearch_to_tsquery tolerates arbitrary user input; ts_rank is deterministic per (row, query),
        // so the rank captured on one page is a stable keyset resume point on the next.
        command.CommandText =
            $"""
            SELECT {FeedColumns},
                   ts_rank(search_tsv, query) AS rank
            FROM forum_content.thread_feed_v, websearch_to_tsquery('simple', @query) AS query
            WHERE search_tsv @@ query
              AND (@cursorId IS NULL
                   OR ts_rank(search_tsv, query) < @cursorRank
                   OR (ts_rank(search_tsv, query) = @cursorRank AND created_on_utc < @cursorCreated)
                   OR (ts_rank(search_tsv, query) = @cursorRank AND created_on_utc = @cursorCreated AND id < @cursorId))
            ORDER BY rank DESC, created_on_utc DESC, id DESC
            LIMIT @limit
            """;
        command.AddParameter("@query", query, DbType.String);
        command.AddParameter("@cursorId", cursor?.Id.ToString() ?? (object)DBNull.Value, DbType.String);
        command.AddParameter("@cursorRank", cursor?.Rank ?? (object)DBNull.Value, DbType.Single);
        command.AddParameter("@cursorCreated", cursor?.CreatedOnUtc ?? (object)DBNull.Value, DbType.DateTimeOffset);
        command.AddParameter("@limit", limit + 1);

        var ranks = new List<float>(limit + 1);
        var items = await ReadFeedRowsAsync(
            connection, command, limit, cancellationToken, reader => ranks.Add(reader.GetFloat(13)));
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var last = items.Count > 0 ? items[^1] : null;
        var nextCursor = hasMore && last is not null
            ? new ThreadSearchCursor(ranks[items.Count - 1], last.CreatedOnUtc, last.Id).Encode()
            : null;

        return new CursorPage<ThreadFeedItemResponse>(items, nextCursor, hasMore);
    }

    public async Task<ThreadDetailResponse?> GetThreadAsync(Ulid threadId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, category_id, category_slug, category_name, title, body, is_pinned,
                   owner_id, username, display_name, tag_slugs, created_on_utc, last_modified_on_utc
            FROM forum_content.thread_detail_v
            WHERE id = @id
            """;
        command.AddParameter("@id", threadId.ToString(), DbType.String);

        var opened = await EnsureOpenAsync(connection, cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new ThreadDetailResponse(
                ReadUlid(reader, 0),
                ReadUlid(reader, 1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                ReadUlid(reader, 7),
                reader.GetString(8),
                reader.GetString(9),
                await reader.GetFieldValueAsync<string[]>(10, cancellationToken),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<CommentResponse>> GetCommentTreeAsync(
        Ulid threadId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, thread_id, parent_id, path, depth, body, is_deleted,
                   owner_id, username, display_name, created_on_utc
            FROM forum_content.comment_tree_v
            WHERE thread_id = @threadId
            ORDER BY path
            """;
        command.AddParameter("@threadId", threadId.ToString(), DbType.String);

        var opened = await EnsureOpenAsync(connection, cancellationToken);
        try
        {
            var items = new List<CommentResponse>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CommentResponse(
                    ReadUlid(reader, 0),
                    ReadUlid(reader, 1),
                    reader.IsDBNull(2) ? null : ReadUlid(reader, 2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetBoolean(6),
                    ReadUlid(reader, 7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetFieldValue<DateTimeOffset>(10)));
            }

            return items;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<CursorPage<ThreadFeedItemResponse>> GetThreadsByOwnerAsync(
        Ulid ownerId, OwnerActivityCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();

        // Plain chronological keyset — is_pinned is a category-feed concept, not an activity one.
        command.CommandText =
            $"""
            SELECT {FeedColumns}
            FROM forum_content.thread_feed_v
            WHERE owner_id = @ownerId
              AND (@cursorId IS NULL
                   OR created_on_utc < @cursorCreated
                   OR (created_on_utc = @cursorCreated AND id < @cursorId))
            ORDER BY created_on_utc DESC, id DESC
            LIMIT @limit
            """;
        command.AddParameter("@ownerId", ownerId.ToString(), DbType.String);
        command.AddParameter("@cursorId", cursor?.Id.ToString() ?? (object)DBNull.Value, DbType.String);
        command.AddParameter("@cursorCreated", cursor?.CreatedOnUtc ?? (object)DBNull.Value, DbType.DateTimeOffset);
        command.AddParameter("@limit", limit + 1);

        var items = await ReadFeedRowsAsync(connection, command, limit, cancellationToken);
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var last = items.Count > 0 ? items[^1] : null;
        var nextCursor = hasMore && last is not null
            ? new OwnerActivityCursor(last.CreatedOnUtc, last.Id).Encode()
            : null;

        return new CursorPage<ThreadFeedItemResponse>(items, nextCursor, hasMore);
    }

    public async Task<CursorPage<CommentActivityItemResponse>> GetCommentsByOwnerAsync(
        Ulid ownerId, OwnerActivityCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();

        // Tombstoned comments and comments on deleted threads are noise in a profile timeline —
        // both filters are deliberate (comment_tree_v keeps them for the tree view instead).
        command.CommandText =
            """
            SELECT c.id, c.thread_id, t.title, c.body, c.created_on_utc
            FROM forum_content.comments AS c
            JOIN forum_content.threads AS t ON t.id = c.thread_id AND t.is_deleted = false
            WHERE c.owner_id = @ownerId
              AND c.is_deleted = false
              AND (@cursorId IS NULL
                   OR c.created_on_utc < @cursorCreated
                   OR (c.created_on_utc = @cursorCreated AND c.id < @cursorId))
            ORDER BY c.created_on_utc DESC, c.id DESC
            LIMIT @limit
            """;
        command.AddParameter("@ownerId", ownerId.ToString(), DbType.String);
        command.AddParameter("@cursorId", cursor?.Id.ToString() ?? (object)DBNull.Value, DbType.String);
        command.AddParameter("@cursorCreated", cursor?.CreatedOnUtc ?? (object)DBNull.Value, DbType.DateTimeOffset);
        command.AddParameter("@limit", limit + 1);

        var opened = await EnsureOpenAsync(connection, cancellationToken);
        List<CommentActivityItemResponse> items;
        try
        {
            items = new List<CommentActivityItemResponse>(limit + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CommentActivityItemResponse(
                    ReadUlid(reader, 0),
                    ReadUlid(reader, 1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        var last = items.Count > 0 ? items[^1] : null;
        var nextCursor = hasMore && last is not null
            ? new OwnerActivityCursor(last.CreatedOnUtc, last.Id).Encode()
            : null;

        return new CursorPage<CommentActivityItemResponse>(items, nextCursor, hasMore);
    }

    public async Task<IReadOnlyList<TagSuggestionResponse>> SuggestTagsAsync(
        string? slugFilter, int limit, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();

        // Usage counts only live threads (the join condition drops soft-deleted ones), but a
        // zero-usage tag still lists — autocomplete should offer every known slug.
        command.CommandText =
            """
            SELECT t.slug, t.name, COUNT(th.id)::int AS usage_count
            FROM forum_content.tags AS t
            LEFT JOIN forum_content.thread_tags AS tt ON tt.tag_id = t.id
            LEFT JOIN forum_content.threads AS th ON th.id = tt.thread_id AND th.is_deleted = false
            WHERE @pattern IS NULL OR t.slug LIKE @pattern
            GROUP BY t.slug, t.name
            ORDER BY usage_count DESC, t.slug
            LIMIT @limit
            """;
        command.AddParameter(
            "@pattern",
            slugFilter is null ? (object)DBNull.Value : $"%{EscapeLike(slugFilter)}%",
            DbType.String);
        command.AddParameter("@limit", limit);

        var opened = await EnsureOpenAsync(connection, cancellationToken);
        try
        {
            var items = new List<TagSuggestionResponse>(limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new TagSuggestionResponse(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2)));
            }

            return items;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string EscapeLike(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal)
        .Replace("_", "\\_", StringComparison.Ordinal);

    private static async Task<List<ThreadFeedItemResponse>> ReadFeedRowsAsync(
        DbConnection connection,
        DbCommand command,
        int limit,
        CancellationToken cancellationToken,
        Action<DbDataReader>? onRow = null)
    {
        var opened = await EnsureOpenAsync(connection, cancellationToken);
        try
        {
            var items = new List<ThreadFeedItemResponse>(limit + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ThreadFeedItemResponse(
                    ReadUlid(reader, 0),
                    ReadUlid(reader, 1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetBoolean(5),
                    ReadUlid(reader, 6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetInt32(9),
                    reader.GetInt32(10),
                    reader.GetFieldValue<DateTimeOffset>(11),
                    reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)));
                onRow?.Invoke(reader);
            }

            return items;
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

    private static CategoryResponse MapCategory(Category category) => new(
        category.Id,
        category.Slug,
        category.Name,
        category.Description,
        category.Visibility.ToString().ToLowerInvariant(),
        category.OwnerId,
        category.IconFileId,
        category.CreatedOnUtc);
}
