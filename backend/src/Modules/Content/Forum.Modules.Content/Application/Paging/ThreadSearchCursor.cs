using System.Globalization;

using Forum.Modules.Content.Application.Threads;

namespace Forum.Modules.Content.Application.Paging;

/// <summary>
/// Keyset cursor for search results. The sort travels inside the cursor, because the resume point of each order
/// is a different key: relevance resumes at (<c>rank DESC, created_on_utc DESC, id DESC</c>) — <c>ts_rank</c> is
/// deterministic for a given row + query, so the rank read from one page is a stable resume point — and newest at
/// (<c>created_on_utc DESC, id DESC</c>). A cursor presented with the other sort is rejected (422), never reinterpreted.
/// </summary>
internal sealed record ThreadSearchCursor(ThreadSearchSort Sort, float? Rank, DateTimeOffset CreatedOnUtc, Ulid Id)
{
    private const string RelevanceTag = "r";
    private const string NewestTag = "n";

    public static ThreadSearchCursor ForRelevance(float rank, DateTimeOffset createdOnUtc, Ulid id) =>
        new(ThreadSearchSort.Relevance, rank, createdOnUtc, id);

    public static ThreadSearchCursor ForNewest(DateTimeOffset createdOnUtc, Ulid id) =>
        new(ThreadSearchSort.Newest, null, createdOnUtc, id);

    public string Encode() => Sort == ThreadSearchSort.Newest
        ? CursorCodec.Encode(FormattableString.Invariant($"{NewestTag}|{CreatedOnUtc.UtcTicks}|{Id}"))
        : CursorCodec.Encode(FormattableString.Invariant(
            $"{RelevanceTag}|{(Rank ?? 0f).ToString("R", CultureInfo.InvariantCulture)}|{CreatedOnUtc.UtcTicks}|{Id}"));

    /// <summary>Null when the value is not a cursor this endpoint issued.</summary>
    public static ThreadSearchCursor? TryDecode(string value)
    {
        if (CursorCodec.TryDecode(value) is not { } payload)
        {
            return null;
        }

        var parts = payload.Split('|');
        switch (parts[0])
        {
            case RelevanceTag when parts.Length == 4
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var rank)
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var relevanceTicks)
                && Ulid.TryParse(parts[3], CultureInfo.InvariantCulture, out var relevanceId):
                return ForRelevance(rank, new DateTimeOffset(relevanceTicks, TimeSpan.Zero), relevanceId);
            case NewestTag when parts.Length == 3
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var newestTicks)
                && Ulid.TryParse(parts[2], CultureInfo.InvariantCulture, out var newestId):
                return ForNewest(new DateTimeOffset(newestTicks, TimeSpan.Zero), newestId);
            default:
                return null;
        }
    }
}
