using Forum.Common.Paging;
using Forum.Common.Telemetry;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Application.Comments;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Threads;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Forum.Modules.Content.Infrastructure.Caching;

/// <summary>
/// <see cref="IContentReadCache"/> on .NET's <see cref="HybridCache"/> used as a pure in-process L1 (no distributed
/// cache is registered — contract §4 "in-process, per replica"). What the library gives, verified against its 10.2.0
/// source before this was designed around it:
/// <list type="bullet">
/// <item><b>Stampede protection</b>: concurrent misses on one key share one computation (<c>StampedeState</c>); a
/// caller whose token is cancelled stops waiting while the flight continues for the others and is only cancelled when
/// the last caller leaves (B: <c>singleflight</c> + <c>context.WithoutCancel</c>).</item>
/// <item><b>No per-hit deserialization</b>: <see cref="ReadCacheEntry{T}"/> is sealed + <c>[ImmutableObject(true)]</c>,
/// the only shape whose stored instance the L1 returns as-is.</item>
/// <item><b>NOT a generation guard</b>: its tag invalidation (<c>RemoveByTagAsync</c>) expires entries stored before
/// the call, but a value whose computation was in flight during the call is re-stamped with a later timestamp and
/// stored (<c>StampedeState.BackgroundFetchAsync</c>, "timing problem with tag invalidation") — the opposite of §3's
/// rule, since that computation may have read the data BEFORE the write. Invalidation therefore uses generations in
/// the key (<see cref="ReadCacheState"/>): a flush bumps a counter, readers move to the new key at once, and a value
/// computed across the flush stays under the old key — returned to its waiters, never served again, expired by its
/// TTL. Exactly B's <c>key + gen</c> singleflight key.</item>
/// </list>
/// The factory of a miss runs in its own DI scope: HybridCache queues it with
/// <c>ThreadPool.UnsafeQueueUserWorkItem</c> and keeps it alive after the leading request was cancelled, so it must
/// not borrow the request's scoped <c>DbContext</c> (which that request's disposal would pull from under the waiters)
/// — it opens a fresh scope, reads through the module's own <see cref="IContentQueries"/> and disposes it.
/// </summary>
internal sealed class HybridContentReadCache : IContentReadCache
{
    private const string FeedSort = "pinned-newest"; // the only feed order A has (ix_threads_feed)
    private const int MaxKeyLength = 512; // HybridCache logs + skips keys over its 1 024 limit; cut earlier, quietly

    private readonly HybridCache _cache;
    private readonly IContentQueries _requestQueries;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ReadCacheState _state;
    private readonly ReadCacheOptions _options;
    private readonly ForumMetrics _metrics;
    private readonly HybridCacheEntryOptions _feedEntry;
    private readonly HybridCacheEntryOptions _commentsEntry;
    private readonly HybridCacheEntryOptions _searchEntry;

    public HybridContentReadCache(
        HybridCache cache,
        IContentQueries requestQueries,
        IServiceScopeFactory scopeFactory,
        ReadCacheState state,
        IOptions<ReadCacheOptions> options,
        ForumMetrics metrics)
    {
        _cache = cache;
        _requestQueries = requestQueries;
        _scopeFactory = scopeFactory;
        _state = state;
        _options = options.Value;
        _metrics = metrics;
        _feedEntry = EntryOptions(_options.FeedTtlSeconds);
        _commentsEntry = EntryOptions(_options.CommentTreeTtlSeconds);
        _searchEntry = EntryOptions(_options.SearchTtlSeconds);
    }

    public bool Enabled => _options.Enabled;

    public ValueTask<CursorPage<ThreadFeedItemResponse>> GetFeedPageAsync(
        Ulid categoryId, ThreadFeedCursor? cursor, int limit, CancellationToken cancellationToken)
    {
        // The cursor is re-encoded from its decoded form so the key is canonical whatever the client sent.
        var key = $"feed|{categoryId}|{FeedSort}|{cursor?.Encode()}|{limit}|g{_state.FeedGeneration}";
        return GetOrCreateAsync(
            ForumMetrics.ReadCacheFeed, key, _feedEntry, _state.FeedBound,
            new FeedRequest(categoryId, cursor, limit),
            static (queries, request, token) =>
                queries.GetThreadFeedAsync(request.CategoryId, request.Cursor, request.Limit, token),
            cancellationToken);
    }

    public ValueTask<IReadOnlyList<CommentResponse>> GetCommentTreeAsync(Ulid threadId, CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            ForumMetrics.ReadCacheComments,
            $"comments|{threadId}|g{_state.CommentsGeneration}.{_state.ThreadGeneration(threadId)}",
            _commentsEntry, _state.CommentsBound, threadId,
            static (queries, id, token) => queries.GetCommentTreeAsync(id, token),
            cancellationToken);

    public ValueTask<CursorPage<ThreadFeedItemResponse>> SearchAsync(
        string viewerScope,
        string query,
        ThreadSearchSort sort,
        ThreadSearchCursor? cursor,
        int limit,
        IReadOnlyCollection<Ulid> readablePrivateCategoryIds,
        CancellationToken cancellationToken)
    {
        // The query is already trimmed by the handler and enters the key verbatim (B parity: trim only, case-sensitive).
        var key = $"search|{viewerScope}|{query.Trim()}|{sort.ToValue()}|{cursor?.Encode()}|{limit}|g{_state.SearchGeneration}";
        return GetOrCreateAsync(
            ForumMetrics.ReadCacheSearch, key, _searchEntry, _state.SearchBound,
            new SearchRequest(query, sort, cursor, limit, readablePrivateCategoryIds),
            static (queries, request, token) => queries.SearchThreadsAsync(
                request.Query, request.Sort, request.Cursor, request.Limit, request.ReadablePrivateCategoryIds, token),
            cancellationToken);
    }

    public ValueTask ThreadChangedAsync()
    {
        if (_options.Enabled)
        {
            _state.FlushFeed();
            _state.FlushAllCommentTrees();
            _state.FlushSearch();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CommentWrittenAsync(Ulid threadId)
    {
        if (_options.Enabled)
        {
            _state.FlushCommentTree(threadId);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CommentDeletedAsync()
    {
        if (_options.Enabled)
        {
            _state.FlushFeed();
            _state.FlushAllCommentTrees();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask CategoryChangedAsync() => ThreadChangedAsync();

    private static HybridCacheEntryOptions EntryOptions(double ttlSeconds) => new()
    {
        Expiration = TimeSpan.FromSeconds(ttlSeconds),
        LocalCacheExpiration = TimeSpan.FromSeconds(ttlSeconds),
        Flags = HybridCacheEntryFlags.DisableDistributedCache,
    };

    /// <summary>
    /// HybridCache refuses keys over its length limit or containing control characters by logging and running the
    /// callback uncached — on every call. A free-text search query can produce both; decide here, without the log.
    /// </summary>
    private static bool IsCacheableKey(string key)
    {
        if (key.Length > MaxKeyLength)
        {
            return false;
        }

        foreach (var character in key)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private async ValueTask<T> GetOrCreateAsync<TRequest, T>(
        string cacheName,
        string key,
        HybridCacheEntryOptions entryOptions,
        BoundedKeySet bound,
        TRequest request,
        Func<IContentQueries, TRequest, CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
        where T : class
    {
        if (!_options.Enabled || !IsCacheableKey(key))
        {
            // Switched off (or an uncacheable key): the store, on the request's own scope — no lookup, no stampede.
            _metrics.ReadCacheLookup(cacheName, ForumMetrics.ReadCacheBypass);
            return await read(_requestQueries, request, cancellationToken);
        }

        var state = new FactoryState<TRequest, T>(_scopeFactory, request, read);
        var entry = await _cache.GetOrCreateAsync(
            key,
            state,
            static async (state, token) =>
            {
                state.Ran = true;
                await using var scope = state.ScopeFactory.CreateAsyncScope();
                var queries = scope.ServiceProvider.GetRequiredService<IContentQueries>();
                return new ReadCacheEntry<T>(await state.Read(queries, state.Request, token));
            },
            entryOptions,
            tags: null,
            cancellationToken);

        if (state.Ran)
        {
            _metrics.ReadCacheLookup(cacheName, ForumMetrics.ReadCacheMiss);
            if (bound.Track(key) is { } evicted)
            {
                await _cache.RemoveAsync(evicted, CancellationToken.None);
            }
        }
        else
        {
            _metrics.ReadCacheLookup(cacheName, ForumMetrics.ReadCacheHit);
        }

        return entry.Value;
    }

    private sealed record FeedRequest(Ulid CategoryId, ThreadFeedCursor? Cursor, int Limit);

    private sealed record SearchRequest(
        string Query,
        ThreadSearchSort Sort,
        ThreadSearchCursor? Cursor,
        int Limit,
        IReadOnlyCollection<Ulid> ReadablePrivateCategoryIds);

    /// <summary>The per-call state HybridCache hands the (static) factory; <see cref="Ran"/> tells hit from miss.</summary>
    private sealed class FactoryState<TRequest, T>
    {
        public FactoryState(
            IServiceScopeFactory scopeFactory, TRequest request, Func<IContentQueries, TRequest, CancellationToken, Task<T>> read)
        {
            ScopeFactory = scopeFactory;
            Request = request;
            Read = read;
        }

        public IServiceScopeFactory ScopeFactory { get; }

        public TRequest Request { get; }

        public Func<IContentQueries, TRequest, CancellationToken, Task<T>> Read { get; }

        public bool Ran { get; set; }
    }
}
