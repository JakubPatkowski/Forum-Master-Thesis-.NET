using System.Globalization;

namespace Forum.Modules.Content.Application.Paging;

/// <summary>
/// Keyset cursor for FTS results, matching the search sort key (<c>rank DESC, created_on_utc DESC, id DESC</c>).
/// <c>ts_rank</c> is deterministic for a given row + query, so the rank read from one page is a stable resume point.
/// </summary>
internal sealed record ThreadSearchCursor(float Rank, DateTimeOffset CreatedOnUtc, Ulid Id)
{
    public string Encode() => CursorCodec.Encode(FormattableString.Invariant(
        $"{Rank.ToString("R", CultureInfo.InvariantCulture)}|{CreatedOnUtc.UtcTicks}|{Id}"));

    /// <summary>Null when the value is not a cursor this endpoint issued.</summary>
    public static ThreadSearchCursor? TryDecode(string value)
    {
        if (CursorCodec.TryDecode(value) is not { } payload)
        {
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length != 3
            || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rank)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !Ulid.TryParse(parts[2], CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        return new ThreadSearchCursor(rank, new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }
}
