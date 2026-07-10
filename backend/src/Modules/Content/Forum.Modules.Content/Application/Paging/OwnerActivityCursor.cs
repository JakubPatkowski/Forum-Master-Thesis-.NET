using System.Globalization;

namespace Forum.Modules.Content.Application.Paging;

/// <summary>
/// Keyset cursor for owner-filtered activity reads (threads/comments by a user). Plain
/// chronological sort key (<c>created_on_utc DESC, id DESC</c>) — pinning is a category-feed
/// concept and deliberately plays no part in a user's own timeline.
/// </summary>
internal sealed record OwnerActivityCursor(DateTimeOffset CreatedOnUtc, Ulid Id)
{
    public string Encode() => CursorCodec.Encode(FormattableString.Invariant(
        $"{CreatedOnUtc.UtcTicks}|{Id}"));

    /// <summary>Null when the value is not a cursor this endpoint issued.</summary>
    public static OwnerActivityCursor? TryDecode(string value)
    {
        if (CursorCodec.TryDecode(value) is not { } payload)
        {
            return null;
        }

        var parts = payload.Split('|');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || !Ulid.TryParse(parts[1], CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        return new OwnerActivityCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);
    }
}
