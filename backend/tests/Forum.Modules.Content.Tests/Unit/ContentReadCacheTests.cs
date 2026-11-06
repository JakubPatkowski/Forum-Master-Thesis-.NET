using Forum.Common.Paging;
using Forum.Common.Telemetry;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Application.Comments;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Threads;
using Forum.Modules.Content.Infrastructure.Caching;
using Forum.TestUtilities;

using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Content.Tests.Unit;

/// <summary>
/// The read cache against the REAL HybridCache (pure L1, as registered by ContentModule): the contract's key
/// schemas (§2), every §3 invalidation row, the generation guard, the stored-instance (no deserialization) property,
/// the disabled switch and the per-cache bound. The store is a substitute whose call count is the oracle.
/// (The generation-guard test is what showed HybridCache's tag invalidation re-stamps an in-flight value — see the
/// cache's class remarks; the key-generation design replaced tags because of it.)
/// </summary>
public sealed class ContentReadCacheTests
{
    private static readonly Ulid CategoryA = Ulid.NewUlid();
    private static readonly Ulid CategoryB = Ulid.NewUlid();
    private static readonly Ulid ThreadA = Ulid.NewUlid();
    private static readonly Ulid ThreadB = Ulid.NewUlid();

    [Fact]
    public async Task Feed_pages_are_keyed_by_category_cursor_and_limit_and_a_hit_returns_the_stored_instance()
    {
        var (cache, queries, capture) = Build(enabled: true);
        var cursor = new ThreadFeedCursor(false, DateTimeOffset.UtcNow, Ulid.NewUlid());

        var first = await cache.GetFeedPageAsync(CategoryA, null, 20, CancellationToken.None);
        var again = await cache.GetFeedPageAsync(CategoryA, null, 20, CancellationToken.None);
        await cache.GetFeedPageAsync(CategoryA, cursor, 20, CancellationToken.None);
        await cache.GetFeedPageAsync(CategoryA, null, 10, CancellationToken.None);
        await cache.GetFeedPageAsync(CategoryB, null, 20, CancellationToken.None);

        // Same (category, cursor, limit) → one store read; every other combination is its own entry.
        await queries.Received(1).GetThreadFeedAsync(CategoryA, null, 20, Arg.Any<CancellationToken>());
        await queries.Received(1).GetThreadFeedAsync(CategoryA, cursor, 20, Arg.Any<CancellationToken>());
        await queries.Received(1).GetThreadFeedAsync(CategoryA, null, 10, Arg.Any<CancellationToken>());
        await queries.Received(1).GetThreadFeedAsync(CategoryB, null, 20, Arg.Any<CancellationToken>());
        ReferenceEquals(first, again).ShouldBeTrue("the L1 must hand back the stored instance, not a deserialized copy");

        Lookups(capture, ForumMetrics.ReadCacheFeed, ForumMetrics.ReadCacheMiss).ShouldBe(4);
        Lookups(capture, ForumMetrics.ReadCacheFeed, ForumMetrics.ReadCacheHit).ShouldBe(1);
    }

    [Fact]
    public async Task Search_pages_are_keyed_by_viewer_scope_trimmed_query_sort_cursor_and_limit()
    {
        var (cache, queries, _) = Build(enabled: true);
        var anonymous = ReadCacheScope.Of(null, []);
        var user = Ulid.NewUlid();
        var member = ReadCacheScope.Of(user, [CategoryA]);
        var revoked = ReadCacheScope.Of(user, []);

        await cache.SearchAsync(anonymous, "kubernetes", ThreadSearchSort.Relevance, null, 20, [], CancellationToken.None);
        await cache.SearchAsync(anonymous, "  kubernetes ", ThreadSearchSort.Relevance, null, 20, [], CancellationToken.None);
        await cache.SearchAsync(anonymous, "Kubernetes", ThreadSearchSort.Relevance, null, 20, [], CancellationToken.None);
        await cache.SearchAsync(anonymous, "kubernetes", ThreadSearchSort.Newest, null, 20, [], CancellationToken.None);
        await cache.SearchAsync(member, "kubernetes", ThreadSearchSort.Relevance, null, 20, [CategoryA], CancellationToken.None);
        await cache.SearchAsync(revoked, "kubernetes", ThreadSearchSort.Relevance, null, 20, [], CancellationToken.None);

        // Trim-only, case-sensitive (B parity): the padded spelling shares the entry, the capitalized one does not.
        await queries.Received(1).SearchThreadsAsync(
            "Kubernetes", ThreadSearchSort.Relevance, null, 20, Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>());
        await queries.Received(1).SearchThreadsAsync(
            "kubernetes", ThreadSearchSort.Newest, null, 20, Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>());
        // A member's page is computed with its readable set; the revoked scope is a different key → a fresh read.
        await queries.Received(1).SearchThreadsAsync(
            "kubernetes", ThreadSearchSort.Relevance, null, 20, Arg.Is<IReadOnlyCollection<Ulid>>(r => r.Contains(CategoryA)),
            Arg.Any<CancellationToken>());
        await queries.Received(2).SearchThreadsAsync(
            "kubernetes", ThreadSearchSort.Relevance, null, 20, Arg.Is<IReadOnlyCollection<Ulid>>(static r => r.Count == 0),
            Arg.Any<CancellationToken>());
        member.ShouldNotBe(revoked);
        anonymous.ShouldBe("anonymous");
    }

    [Fact]
    public async Task Invalidation_rows_of_the_contract_flush_exactly_the_listed_caches()
    {
        var (cache, queries, _) = Build(enabled: true);

        async Task WarmEverything()
        {
            await cache.GetFeedPageAsync(CategoryA, null, 20, CancellationToken.None);
            await cache.GetCommentTreeAsync(ThreadA, CancellationToken.None);
            await cache.GetCommentTreeAsync(ThreadB, CancellationToken.None);
            await cache.SearchAsync("anonymous", "q", ThreadSearchSort.Relevance, null, 20, [], CancellationToken.None);
        }

        // Row: thread create/update/delete/pin/move → C1 all, C2 all, C3 all.
        await WarmEverything();
        queries.ClearReceivedCalls();
        await cache.ThreadChangedAsync();
        await WarmEverything();
        await queries.Received(1).GetThreadFeedAsync(CategoryA, null, 20, Arg.Any<CancellationToken>());
        await queries.Received(1).GetCommentTreeAsync(ThreadA, Arg.Any<CancellationToken>());
        await queries.Received(1).GetCommentTreeAsync(ThreadB, Arg.Any<CancellationToken>());
        await queries.Received(1).SearchThreadsAsync(
            "q", ThreadSearchSort.Relevance, null, 20, Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>());

        // Row: comment create/update → C2 of THAT thread only.
        queries.ClearReceivedCalls();
        await cache.CommentWrittenAsync(ThreadA);
        await WarmEverything();
        await queries.DidNotReceive().GetThreadFeedAsync(Arg.Any<Ulid>(), Arg.Any<ThreadFeedCursor?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await queries.Received(1).GetCommentTreeAsync(ThreadA, Arg.Any<CancellationToken>());
        await queries.DidNotReceive().GetCommentTreeAsync(ThreadB, Arg.Any<CancellationToken>());
        await queries.DidNotReceive().SearchThreadsAsync(
            Arg.Any<string>(), Arg.Any<ThreadSearchSort>(), Arg.Any<ThreadSearchCursor?>(), Arg.Any<int>(),
            Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>());

        // Row: comment delete → C1 all, C2 all; search untouched.
        queries.ClearReceivedCalls();
        await cache.CommentDeletedAsync();
        await WarmEverything();
        await queries.Received(1).GetThreadFeedAsync(CategoryA, null, 20, Arg.Any<CancellationToken>());
        await queries.Received(1).GetCommentTreeAsync(ThreadA, Arg.Any<CancellationToken>());
        await queries.Received(1).GetCommentTreeAsync(ThreadB, Arg.Any<CancellationToken>());
        await queries.DidNotReceive().SearchThreadsAsync(
            Arg.Any<string>(), Arg.Any<ThreadSearchSort>(), Arg.Any<ThreadSearchCursor?>(), Arg.Any<int>(),
            Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>());

        // Row: space update/delete → C1 all, C2 all, C3 all.
        queries.ClearReceivedCalls();
        await cache.CategoryChangedAsync();
        await WarmEverything();
        await queries.Received(1).GetThreadFeedAsync(CategoryA, null, 20, Arg.Any<CancellationToken>());
        await queries.Received(1).GetCommentTreeAsync(ThreadA, Arg.Any<CancellationToken>());
        await queries.Received(1).SearchThreadsAsync(
            "q", ThreadSearchSort.Relevance, null, 20, Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>());

        // No write in between → everything is a hit.
        queries.ClearReceivedCalls();
        await WarmEverything();
        queries.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_value_computed_across_an_invalidation_is_returned_to_its_waiters_but_never_served_again()
    {
        var (cache, queries, _) = Build(enabled: true);
        var gate = new TaskCompletionSource<IReadOnlyList<CommentResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        queries.GetCommentTreeAsync(ThreadA, Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref reads) == 1 ? gate.Task : Task.FromResult(Tree("fresh")));

        // The first read is in flight (blocked on the gate) when a comment lands on the thread.
        var inFlight = cache.GetCommentTreeAsync(ThreadA, CancellationToken.None).AsTask();
        await TestWaitUntil(() => Volatile.Read(ref reads) == 1);
        await cache.CommentWrittenAsync(ThreadA);
        gate.SetResult(Tree("stale"));

        (await inFlight)[0].Body.ShouldBe("stale", "the waiters of the computation get the value they waited for");

        // …but the entry stored by that computation predates the invalidation: the next read goes to the store.
        (await cache.GetCommentTreeAsync(ThreadA, CancellationToken.None))[0].Body.ShouldBe("fresh");
        reads.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_misses_on_one_key_share_a_single_store_read()
    {
        var (cache, queries, capture) = Build(enabled: true);
        var gate = new TaskCompletionSource<IReadOnlyList<CommentResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        queries.GetCommentTreeAsync(ThreadA, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref reads);
                return gate.Task;
            });

        var waiters = Enumerable.Range(0, 8)
            .Select(_ => cache.GetCommentTreeAsync(ThreadA, CancellationToken.None).AsTask())
            .ToArray();
        await TestWaitUntil(() => Volatile.Read(ref reads) == 1);
        await Task.Delay(50); // give every waiter the chance to (wrongly) start its own read
        gate.SetResult(Tree("shared"));

        var results = await Task.WhenAll(waiters);
        results.ShouldAllBe(static tree => tree[0].Body == "shared");
        reads.ShouldBe(1, "stampede protection: one computation per key");
        Lookups(capture, ForumMetrics.ReadCacheComments, ForumMetrics.ReadCacheMiss).ShouldBe(1);
        Lookups(capture, ForumMetrics.ReadCacheComments, ForumMetrics.ReadCacheHit).ShouldBe(7, "joined waiters count as hits");
    }

    [Fact]
    public async Task Disabled_switch_means_every_read_hits_the_store_and_nothing_is_stored()
    {
        var (cache, queries, capture, memory) = BuildWithMemory(enabled: false);

        await cache.GetFeedPageAsync(CategoryA, null, 20, CancellationToken.None);
        await cache.GetFeedPageAsync(CategoryA, null, 20, CancellationToken.None);
        await cache.GetCommentTreeAsync(ThreadA, CancellationToken.None);
        await cache.SearchAsync("anonymous", "q", ThreadSearchSort.Newest, null, 20, [], CancellationToken.None);
        await cache.ThreadChangedAsync();

        cache.Enabled.ShouldBeFalse();
        await queries.Received(2).GetThreadFeedAsync(CategoryA, null, 20, Arg.Any<CancellationToken>());
        memory.Count.ShouldBe(0, "no lookup, no stored entry");
        Lookups(capture, ForumMetrics.ReadCacheFeed, ForumMetrics.ReadCacheBypass).ShouldBe(2);
        Lookups(capture, ForumMetrics.ReadCacheComments, ForumMetrics.ReadCacheBypass).ShouldBe(1);
        Lookups(capture, ForumMetrics.ReadCacheSearch, ForumMetrics.ReadCacheBypass).ShouldBe(1);
        capture.For("forum.read_cache.lookups").ShouldAllBe(static m => (string?)m.Tags["result"] == "bypass");
    }

    [Fact]
    public async Task Each_cache_is_bounded_to_the_configured_number_of_entries()
    {
        var (cache, queries, _) = Build(enabled: true, maxEntriesPerCache: 3);
        var threads = Enumerable.Range(0, 4).Select(_ => Ulid.NewUlid()).ToArray();

        foreach (var thread in threads)
        {
            await cache.GetCommentTreeAsync(thread, CancellationToken.None);
        }

        // The fourth entry evicted the oldest (FIFO): the last three are still hits, the first thread reads again.
        queries.ClearReceivedCalls();
        await cache.GetCommentTreeAsync(threads[3], CancellationToken.None);
        await cache.GetCommentTreeAsync(threads[2], CancellationToken.None);
        await cache.GetCommentTreeAsync(threads[1], CancellationToken.None);
        queries.ReceivedCalls().ShouldBeEmpty();
        await cache.GetCommentTreeAsync(threads[0], CancellationToken.None);
        await queries.Received(1).GetCommentTreeAsync(threads[0], Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Entries_expire_after_their_ttl()
    {
        var (cache, queries, _) = Build(enabled: true, ttlSeconds: 0.2);

        await cache.GetCommentTreeAsync(ThreadA, CancellationToken.None);
        await cache.GetCommentTreeAsync(ThreadA, CancellationToken.None);
        await Task.Delay(400);
        await cache.GetCommentTreeAsync(ThreadA, CancellationToken.None);

        await queries.Received(2).GetCommentTreeAsync(ThreadA, Arg.Any<CancellationToken>());
    }

    // ---- fixture ----

    private static (IContentReadCache Cache, IContentQueries Queries, MetricCapture Capture) Build(
        bool enabled, int maxEntriesPerCache = 1_000, double ttlSeconds = 2)
    {
        var (cache, queries, capture, _) = BuildWithMemory(enabled, maxEntriesPerCache, ttlSeconds);
        return (cache, queries, capture);
    }

    private static (IContentReadCache Cache, IContentQueries Queries, MetricCapture Capture, MemoryCache Memory) BuildWithMemory(
        bool enabled, int maxEntriesPerCache = 1_000, double ttlSeconds = 2)
    {
        var queries = Substitute.For<IContentQueries>();
        queries.GetThreadFeedAsync(Arg.Any<Ulid>(), Arg.Any<ThreadFeedCursor?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new CursorPage<ThreadFeedItemResponse>([], null, false)));
        queries.GetCommentTreeAsync(Arg.Any<Ulid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Tree("body")));
        queries.SearchThreadsAsync(
                Arg.Any<string>(), Arg.Any<ThreadSearchSort>(), Arg.Any<ThreadSearchCursor?>(), Arg.Any<int>(),
                Arg.Any<IReadOnlyCollection<Ulid>>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new CursorPage<ThreadFeedItemResponse>([], null, false)));

        var metrics = TestMetrics.Create(out var scope);
        var options = new ReadCacheOptions
        {
            Enabled = enabled,
            FeedTtlSeconds = ttlSeconds,
            CommentTreeTtlSeconds = ttlSeconds,
            SearchTtlSeconds = ttlSeconds,
            MaxEntriesPerCache = maxEntriesPerCache,
        };

        // The same wiring ContentModule does: HybridCache with no IDistributedCache → pure in-process L1.
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddHybridCache(o => o.DefaultEntryOptions = new HybridCacheEntryOptions
        {
            Flags = HybridCacheEntryFlags.DisableDistributedCache,
        });
        services.AddSingleton(metrics);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(new ReadCacheState(options.MaxEntriesPerCache));
        services.AddScoped(_ => queries); // both the request's store and the one a miss resolves in its own scope
        services.AddScoped<IContentReadCache, HybridContentReadCache>();
        var provider = services.BuildServiceProvider();
        var cache = provider.CreateScope().ServiceProvider.GetRequiredService<IContentReadCache>();
        return (cache, queries, new MetricCapture(scope), (MemoryCache)provider.GetRequiredService<IMemoryCache>());
    }

    private static IReadOnlyList<CommentResponse> Tree(string body) =>
    [
        new CommentResponse(Ulid.NewUlid(), ThreadA, null, "x", 0, body, false, Ulid.NewUlid(), "u", "U", DateTimeOffset.UtcNow),
    ];

    private static int Lookups(MetricCapture capture, string cache, string result) =>
        capture.For("forum.read_cache.lookups")
            .Count(m => (string?)m.Tags["cache"] == cache && (string?)m.Tags["result"] == result);

    private static async Task TestWaitUntil(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!condition())
        {
            (DateTimeOffset.UtcNow < deadline).ShouldBeTrue("condition not met in time");
            await Task.Delay(10);
        }
    }
}
