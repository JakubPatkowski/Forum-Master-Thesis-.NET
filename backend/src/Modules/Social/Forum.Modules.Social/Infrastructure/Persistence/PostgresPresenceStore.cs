using System.Globalization;

using Forum.Modules.Social.Application;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Presence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Infrastructure.Persistence;

/// <summary>
/// The non-Redis <see cref="IPresenceStore"/>: one <c>user_presence</c> row per user, upserted in place
/// (INSERT ... ON CONFLICT — a heartbeat is not worth EF change tracking), status computed from heartbeat age at
/// read time with the privacy flag folded into the same round trip. The scoped Redis session replaces THIS class
/// only.
/// </summary>
internal sealed class PostgresPresenceStore : IPresenceStore
{
    private readonly SocialDbContext _db;
    private readonly TimeProvider _clock;
    private readonly SocialOptions _options;

    public PostgresPresenceStore(SocialDbContext db, TimeProvider clock, IOptions<SocialOptions> options)
    {
        _db = db;
        _clock = clock;
        _options = options.Value;
    }

    public Task HeartbeatAsync(Ulid userId, CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO forum_social.user_presence (user_id, last_heartbeat_on_utc)
            VALUES ({0}, {1})
            ON CONFLICT (user_id) DO UPDATE SET last_heartbeat_on_utc = EXCLUDED.last_heartbeat_on_utc
            """,
            [userId.ToString(), _clock.GetUtcNow()],
            cancellationToken);

    public async Task<IReadOnlyDictionary<Ulid, PresenceStatus>> GetStatusesAsync(
        IReadOnlyList<Ulid> userIds, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Ulid, PresenceStatus>(userIds.Count);
        if (userIds.Count == 0)
        {
            return result;
        }

        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.user_id, p.last_heartbeat_on_utc
            FROM forum_social.user_presence p
            LEFT JOIN forum_social.user_privacy_settings s ON s.user_id = p.user_id
            WHERE p.user_id = ANY(@ids) AND COALESCE(s.show_online_status, TRUE)
            """;
        command.AddTextArrayParameter("@ids", [.. userIds.Distinct().Select(static id => id.ToString())]);

        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            var now = _clock.GetUtcNow();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var userId = Ulid.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
                var age = now - reader.GetFieldValue<DateTimeOffset>(1);
                result[userId] = age.TotalSeconds < _options.PresenceOnlineSeconds
                    ? PresenceStatus.Online
                    : age.TotalSeconds < _options.PresenceAwaySeconds
                        ? PresenceStatus.Away
                        : PresenceStatus.Offline;
            }
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }

        return result;
    }
}
