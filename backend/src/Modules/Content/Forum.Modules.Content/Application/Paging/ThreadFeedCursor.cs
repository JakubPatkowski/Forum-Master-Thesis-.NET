using System.Globalization;

namespace Forum.Modules.Content.Application.Paging;

/// <summary>
/// Keyset cursor for the thread feed. Encodes exactly the sort key of <c>ix_threads_feed</c>
/// (<c>is_pinned DESC, created_on_utc DESC, id DESC</c>) so the WHERE clause can resume after the last row.
/// </summary>
internal sealed record ThreadFeedCursor(bool IsPinned, DateTimeOffset CreatedOnUtc, Ulid Id)
{
    public string Encode() => CursorCodec.Encode(FormattableString.Invariant(
        $"{(IsPinned ? 1 : 0)}|{CreatedOnUtc.UtcTicks}|{Id}"));

    /// <summary>Null when the value is not a cursor this endpoint issued.</summary>
    public static ThreadFeedCursor? TryDecode(string value)
    {
        if (CursorCodec.TryDecode(value) is not { } payload)
        {
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length != 3
            || parts[0] is not ("0" or "1")
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !Ulid.TryParse(parts[2], CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        return new ThreadFeedCursor(parts[0] == "1", new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }
}
