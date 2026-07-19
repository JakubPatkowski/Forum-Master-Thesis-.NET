using System.Data;
using System.Data.Common;
using System.Globalization;

using Forum.Common.Paging;
using Forum.Modules.Social.Application.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Social.Infrastructure.Persistence;

/// <summary>
/// Raw-ADO reads over the <c>forum_social</c> views (ContentQueries precedent). Keyset pages fetch
/// <c>limit + 1</c> rows: the extra row is the has-more probe, and the last returned row's ULID becomes the next
/// cursor. Descending ULID order == descending creation order everywhere.
/// </summary>
internal sealed class SocialQueries : ISocialQueries
{
    private readonly SocialDbContext _db;

    public SocialQueries(SocialDbContext db) => _db = db;

    public Task<CursorPage<FriendResponse>> GetFriendsAsync(
        Ulid userId, Ulid? cursor, int limit, CancellationToken cancellationToken) =>
        ReadPageAsync(
            """
            SELECT friendship_id, friend_id, friend_username, friends_since_utc
            FROM forum_social.friend_list_v
            WHERE user_id = @user_id AND (@cursor IS NULL OR friendship_id < @cursor)
            ORDER BY friendship_id DESC
            LIMIT @limit
            """,
            command =>
            {
                command.AddParameter("@user_id", userId.ToString());
                command.AddCursor(cursor);
                command.AddParameter("@limit", limit + 1);
            },
            static reader => new FriendResponse(
                reader.GetUlid(0), reader.GetUlid(1), reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3)),
            static friend => friend.FriendshipId,
            limit,
            cancellationToken);

    public async Task<FriendRequestsResponse> GetFriendRequestsAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var rows = await ReadListAsync(
            """
            SELECT friendship_id, requester_id, requester_username, addressee_id, addressee_username, created_on_utc
            FROM forum_social.friend_request_v
            WHERE requester_id = @user_id OR addressee_id = @user_id
            ORDER BY friendship_id DESC
            LIMIT 200
            """,
            command => command.AddParameter("@user_id", userId.ToString()),
            static reader => new FriendRequestResponse(
                reader.GetUlid(0), reader.GetUlid(1), reader.GetString(2), reader.GetUlid(3), reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5)),
            cancellationToken);

        return new FriendRequestsResponse(
            [.. rows.Where(request => request.AddresseeId == userId)],
            [.. rows.Where(request => request.RequesterId == userId)]);
    }

    public async Task<IReadOnlyList<BlockedUserResponse>> GetBlockedUsersAsync(
        Ulid userId, CancellationToken cancellationToken) =>
        await ReadListAsync(
            """
            SELECT blocked_id, blocked_username, created_on_utc
            FROM forum_social.blocked_list_v
            WHERE blocker_id = @user_id
            ORDER BY created_on_utc DESC
            LIMIT 500
            """,
            command => command.AddParameter("@user_id", userId.ToString()),
            static reader => new BlockedUserResponse(
                reader.GetUlid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)),
            cancellationToken);

    public Task<CursorPage<GroupSummaryResponse>> GetGroupsAsync(
        Ulid viewerId, GroupListFilter filter, Ulid? cursor, int limit, CancellationToken cancellationToken)
    {
        var visibilityClause = filter switch
        {
            GroupListFilter.Mine => "is_member",
            GroupListFilter.Public => "g.visibility = 'public'",
            _ => "(g.visibility = 'public' OR is_member)",
        };

        return ReadPageAsync(
            $"""
            SELECT g.group_id, g.name, g.description, g.visibility, g.owner_id, g.owner_username,
                   g.member_count, is_member, g.created_on_utc
            FROM forum_social.group_list_v g
            CROSS JOIN LATERAL (SELECT EXISTS (
                SELECT 1 FROM forum_social.group_memberships m
                WHERE m.group_id = g.group_id AND m.user_id = @viewer_id) AS is_member) v
            WHERE {visibilityClause} AND (@cursor IS NULL OR g.group_id < @cursor)
            ORDER BY g.group_id DESC
            LIMIT @limit
            """,
            command =>
            {
                command.AddParameter("@viewer_id", viewerId.ToString());
                command.AddCursor(cursor);
                command.AddParameter("@limit", limit + 1);
            },
            static reader => new GroupSummaryResponse(
                reader.GetUlid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetUlid(4), reader.GetString(5), reader.GetInt32(6), reader.GetBoolean(7),
                reader.GetFieldValue<DateTimeOffset>(8)),
            static group => group.GroupId,
            limit,
            cancellationToken);
    }

    public async Task<GroupDetailResponse?> GetGroupAsync(
        Ulid groupId, Ulid viewerId, CancellationToken cancellationToken)
    {
        var rows = await ReadListAsync(
            """
            SELECT g.group_id, g.name, g.description, g.visibility, g.owner_id, g.owner_username, g.member_count,
                   EXISTS (SELECT 1 FROM forum_social.group_memberships m
                           WHERE m.group_id = g.group_id AND m.user_id = @viewer_id) AS is_member,
                   EXISTS (SELECT 1 FROM forum_social.group_member_v gm
                           WHERE gm.group_id = g.group_id AND gm.user_id = @viewer_id AND gm.is_admin) AS is_admin,
                   g.created_on_utc
            FROM forum_social.group_list_v g
            WHERE g.group_id = @group_id
            """,
            command =>
            {
                command.AddParameter("@group_id", groupId.ToString());
                command.AddParameter("@viewer_id", viewerId.ToString());
            },
            static reader => new GroupDetailResponse(
                reader.GetUlid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetUlid(4), reader.GetString(5), reader.GetInt32(6), reader.GetBoolean(7),
                reader.GetBoolean(8), reader.GetFieldValue<DateTimeOffset>(9)),
            cancellationToken);

        return rows.Count == 0 ? null : rows[0];
    }

    public Task<CursorPage<GroupMemberResponse>> GetGroupMembersAsync(
        Ulid groupId, Ulid? cursor, int limit, CancellationToken cancellationToken) =>
        ReadPageAsync(
            """
            SELECT user_id, username, joined_on_utc, is_owner, is_admin
            FROM forum_social.group_member_v
            WHERE group_id = @group_id AND (@cursor IS NULL OR user_id < @cursor)
            ORDER BY user_id DESC
            LIMIT @limit
            """,
            command =>
            {
                command.AddParameter("@group_id", groupId.ToString());
                command.AddCursor(cursor);
                command.AddParameter("@limit", limit + 1);
            },
            static reader => new GroupMemberResponse(
                reader.GetUlid(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetBoolean(3), reader.GetBoolean(4)),
            static member => member.UserId,
            limit,
            cancellationToken);

    public async Task<IReadOnlyList<GroupInviteResponse>> GetMyInvitesAsync(
        Ulid userId, CancellationToken cancellationToken) =>
        await ReadListAsync(
            """
            SELECT invite_id, group_id, group_name, invited_user_id, invited_username,
                   invited_by, invited_by_username, created_on_utc
            FROM forum_social.group_invite_v
            WHERE invited_user_id = @user_id
            ORDER BY invite_id DESC
            LIMIT 200
            """,
            command => command.AddParameter("@user_id", userId.ToString()),
            static reader => new GroupInviteResponse(
                reader.GetUlid(0), reader.GetUlid(1), reader.GetString(2), reader.GetUlid(3), reader.GetString(4),
                reader.GetUlid(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)),
            cancellationToken);

    public async Task<IReadOnlyList<ConversationResponse>> GetConversationsAsync(
        Ulid userId, int limit, CancellationToken cancellationToken) =>
        await ReadListAsync(
            """
            SELECT conversation_id, type, display_name, other_user_id, group_id, last_message_id,
                   last_message_preview, last_message_sender_id, last_message_on_utc, unread_count, is_muted
            FROM forum_social.conversation_list_v
            WHERE user_id = @user_id
            ORDER BY COALESCE(last_message_id, conversation_id) DESC
            LIMIT @limit
            """,
            command =>
            {
                command.AddParameter("@user_id", userId.ToString());
                command.AddParameter("@limit", limit);
            },
            static reader => new ConversationResponse(
                reader.GetUlid(0), reader.GetString(1), reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.GetUlidOrNull(3), reader.GetUlidOrNull(4), reader.GetUlidOrNull(5),
                reader.IsDBNull(6) ? null : reader.GetString(6), reader.GetUlidOrNull(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetInt32(9), reader.GetBoolean(10)),
            cancellationToken);

    public Task<CursorPage<MessageResponse>> GetMessagesAsync(
        Ulid conversationId, Ulid? cursor, int limit, CancellationToken cancellationToken) =>
        ReadPageAsync(
            """
            SELECT message_id, conversation_id, sender_id, sender_username, body,
                   created_on_utc, edited_on_utc, is_deleted
            FROM forum_social.message_history_v
            WHERE conversation_id = @conversation_id AND (@cursor IS NULL OR message_id < @cursor)
            ORDER BY message_id DESC
            LIMIT @limit
            """,
            command =>
            {
                command.AddParameter("@conversation_id", conversationId.ToString());
                command.AddCursor(cursor);
                command.AddParameter("@limit", limit + 1);
            },
            static reader => new MessageResponse(
                reader.GetUlid(0), reader.GetUlid(1), reader.GetUlid(2), reader.GetString(3), reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6), reader.GetBoolean(7)),
            static message => message.MessageId,
            limit,
            cancellationToken);

    public Task<CursorPage<NotificationResponse>> GetNotificationsAsync(
        Ulid userId, bool unreadOnly, Ulid? cursor, int limit, CancellationToken cancellationToken) =>
        ReadPageAsync(
            $"""
            SELECT notification_id, kind, actor_id, actor_username, target_id, is_read, created_on_utc
            FROM forum_social.notification_list_v
            WHERE user_id = @user_id AND (@cursor IS NULL OR notification_id < @cursor)
                  {(unreadOnly ? "AND is_read = false" : string.Empty)}
            ORDER BY notification_id DESC
            LIMIT @limit
            """,
            command =>
            {
                command.AddParameter("@user_id", userId.ToString());
                command.AddCursor(cursor);
                command.AddParameter("@limit", limit + 1);
            },
            static reader => new NotificationResponse(
                reader.GetUlid(0), reader.GetString(1), reader.GetUlidOrNull(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetUlidOrNull(4),
                reader.GetBoolean(5), reader.GetFieldValue<DateTimeOffset>(6)),
            static notification => notification.NotificationId,
            limit,
            cancellationToken);

    public async Task<int> GetUnreadNotificationCountAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var rows = await ReadListAsync(
            "SELECT count(*)::int FROM forum_social.notifications WHERE user_id = @user_id AND is_read = false",
            command => command.AddParameter("@user_id", userId.ToString()),
            static reader => reader.GetInt32(0),
            cancellationToken);
        return rows[0];
    }

    private async Task<CursorPage<T>> ReadPageAsync<T>(
        string sql,
        Action<DbCommand> bind,
        Func<DbDataReader, T> map,
        Func<T, Ulid> cursorOf,
        int limit,
        CancellationToken cancellationToken)
    {
        var rows = await ReadListAsync(sql, bind, map, cancellationToken);
        var hasMore = rows.Count > limit;
        var items = hasMore ? rows.Take(limit).ToArray() : rows;
        return new CursorPage<T>(items, hasMore ? cursorOf(items[^1]).ToString() : null, hasMore);
    }

    private async Task<IReadOnlyList<T>> ReadListAsync<T>(
        string sql, Action<DbCommand> bind, Func<DbDataReader, T> map, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);

        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(map(reader));
            }

            return rows;
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

internal static class SocialReaderExtensions
{
    public static Ulid GetUlid(this DbDataReader reader, int ordinal) =>
        Ulid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    public static Ulid? GetUlidOrNull(this DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Ulid.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    /// <summary>Binds the nullable keyset cursor (typed so "@cursor IS NULL OR id &lt; @cursor" resolves).</summary>
    public static void AddCursor(this DbCommand command, Ulid? cursor) =>
        command.AddParameter("@cursor", cursor?.ToString() ?? (object)DBNull.Value, DbType.String);
}
