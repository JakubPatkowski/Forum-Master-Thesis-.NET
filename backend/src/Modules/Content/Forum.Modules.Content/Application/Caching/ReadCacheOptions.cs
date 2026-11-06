namespace Forum.Modules.Content.Application.Caching;

/// <summary>
/// The Content read cache switch and limits, bound from the <c>ReadCache</c> section (env <c>ReadCache__Enabled</c>).
/// The TTLs are NOT tuning knobs: <c>docs/specs/READ-CACHE-CONTRACT.md</c> §2 fixes them (C1 feed 2 s, C2 comment
/// tree 2 s, C3 search 3 s) for both architectures of the A/B comparison, and §4 bounds every cache at about
/// 1 000 entries. They are options only so a test can shorten them; a measured run keeps the defaults. The default
/// is <b>off</b> (§5): every read goes to the store with no lookup and no stampede layer.
/// </summary>
internal sealed class ReadCacheOptions
{
    public const string SectionName = "ReadCache";

    public bool Enabled { get; init; }

    /// <summary>C1 — one space's feed page (first page and every cursor page).</summary>
    public double FeedTtlSeconds { get; init; } = 2;

    /// <summary>C2 — one thread's whole comment tree.</summary>
    public double CommentTreeTtlSeconds { get; init; } = 2;

    /// <summary>C3 — one search result page (viewer-scoped).</summary>
    public double SearchTtlSeconds { get; init; } = 3;

    /// <summary>Entries kept per cache before the oldest stored key is evicted (contract §4: "about 1 000").</summary>
    public int MaxEntriesPerCache { get; init; } = 1_000;
}
