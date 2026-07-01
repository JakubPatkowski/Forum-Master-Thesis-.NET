using System.Data;
using System.Globalization;

using Forum.Common.Paging;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Administration;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Identity.Infrastructure.Persistence;

/// <summary>Keyset-paged user read model. Compares on the ULID-as-text id (lexicographic == chronological order).</summary>
internal sealed class UserQueries : IUserQueries
{
    private readonly IdentityDbContext _db;

    public UserQueries(IdentityDbContext db) => _db = db;

    public async Task<CursorPage<UserSummaryResponse>> ListAsync(string? cursor, int limit, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, username, display_name, email, status, created_on_utc
            FROM forum_identity.users
            WHERE (@cursor IS NULL OR id > @cursor)
            ORDER BY id
            LIMIT @limit
            """;
        command.AddParameter("@cursor", string.IsNullOrWhiteSpace(cursor) ? DBNull.Value : cursor, DbType.String);
        command.AddParameter("@limit", limit + 1);

        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            var items = new List<UserSummaryResponse>(limit + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new UserSummaryResponse(
                    Ulid.Parse(reader.GetString(0), CultureInfo.InvariantCulture),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }

            var hasMore = items.Count > limit;
            if (hasMore)
            {
                items.RemoveAt(items.Count - 1);
            }

            var nextCursor = hasMore ? items[^1].Id.ToString() : null;
            return new CursorPage<UserSummaryResponse>(items, nextCursor, hasMore);
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
