using System.Collections.Concurrent;

namespace Forum.Modules.Content.Infrastructure.Caching;

/// <summary>
/// The "about 1 000 entries per cache" bound of <c>docs/specs/READ-CACHE-CONTRACT.md</c> §4 for a cache whose L1
/// cannot count its own entries: <c>HybridCache</c> stores immutable instances with no size (so a MemoryCache
/// <c>SizeLimit</c> would reject them). Every stored key is appended; past the capacity the oldest stored key is handed
/// back for removal. A key stored again after it expired is appended again, so the count is an upper bound of the
/// live entries and a re-stored key may be evicted a little early — the contract asks for "about", not exact.
/// </summary>
internal sealed class BoundedKeySet
{
    private readonly ConcurrentQueue<string> _stored = new();
    private readonly int _capacity;
    private int _count;

    public BoundedKeySet(int capacity) => _capacity = Math.Max(1, capacity);

    /// <summary>Records a freshly stored key; returns the key to evict when the bound is exceeded, else null.</summary>
    public string? Track(string key)
    {
        _stored.Enqueue(key);
        if (Interlocked.Increment(ref _count) <= _capacity)
        {
            return null;
        }

        if (_stored.TryDequeue(out var oldest))
        {
            Interlocked.Decrement(ref _count);
            return oldest;
        }

        return null;
    }
}

/// <summary>
/// The replica-wide state behind <see cref="HybridContentReadCache"/>: one bound per contract cache (C1, C2, C3) and
/// the invalidation generations. A generation is part of every key, so an invalidation makes the old entries
/// unreachable at once and a value computed ACROSS an invalidation lands under the old generation's key — returned to
/// its waiters, never read again (the §3 generation guard, exactly B's <c>key + gen</c> singleflight key). The unread
/// entries expire with their TTL. Comment trees carry two generations: the cache-wide one (thread and category writes,
/// comment deletes flush every tree) and a per-thread one (a comment create/update flushes only that thread's tree).
/// </summary>
internal sealed class ReadCacheState
{
    // Per-thread generations grow with the number of threads commented on since boot; far past any real forum's
    // working set the table is dropped and the cache-wide generation bumped instead (a one-off mass flush).
    private const int MaxTrackedThreads = 100_000;

    private readonly ConcurrentDictionary<Ulid, long> _threadGenerations = new();
    private long _feed;
    private long _comments;
    private long _search;

    public ReadCacheState(int capacityPerCache)
    {
        FeedBound = new BoundedKeySet(capacityPerCache);
        CommentsBound = new BoundedKeySet(capacityPerCache);
        SearchBound = new BoundedKeySet(capacityPerCache);
    }

    public BoundedKeySet FeedBound { get; }

    public BoundedKeySet CommentsBound { get; }

    public BoundedKeySet SearchBound { get; }

    public long FeedGeneration => Volatile.Read(ref _feed);

    public long SearchGeneration => Volatile.Read(ref _search);

    public long CommentsGeneration => Volatile.Read(ref _comments);

    public long ThreadGeneration(Ulid threadId) => _threadGenerations.TryGetValue(threadId, out var generation) ? generation : 0;

    public void FlushFeed() => Interlocked.Increment(ref _feed);

    public void FlushSearch() => Interlocked.Increment(ref _search);

    public void FlushAllCommentTrees() => Interlocked.Increment(ref _comments);

    public void FlushCommentTree(Ulid threadId)
    {
        if (_threadGenerations.Count >= MaxTrackedThreads)
        {
            _threadGenerations.Clear();
            FlushAllCommentTrees();
            return;
        }

        _threadGenerations.AddOrUpdate(threadId, static _ => 1, static (_, generation) => generation + 1);
    }
}
