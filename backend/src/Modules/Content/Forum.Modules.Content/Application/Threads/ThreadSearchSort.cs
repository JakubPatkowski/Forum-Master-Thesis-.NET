namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// The order of a search result page. <see cref="Relevance"/> ranks every match (<c>ts_rank</c>, title weighted over
/// body) and is the API default; <see cref="Newest"/> walks <c>created_on_utc DESC, id DESC</c> and lets the planner
/// stop at the page size for common words — the order Architecture B's search page uses by default, mirrored here for
/// the J6 comparison (A/B v5). The SPA's search page defaults to <see cref="Newest"/> like B's UI.
/// </summary>
internal enum ThreadSearchSort
{
    Relevance,
    Newest,
}

internal static class ThreadSearchSortParser
{
    public const string RelevanceValue = "relevance";
    public const string NewestValue = "newest";

    /// <summary>Null for an unknown value; a missing value is the API default (relevance).</summary>
    public static ThreadSearchSort? TryParse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ThreadSearchSort.Relevance;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            RelevanceValue => ThreadSearchSort.Relevance,
            NewestValue => ThreadSearchSort.Newest,
            _ => null,
        };
    }

    public static string ToValue(this ThreadSearchSort sort) =>
        sort == ThreadSearchSort.Newest ? NewestValue : RelevanceValue;
}
