using System.ComponentModel;

using Forum.Common.Paging;
using Forum.Modules.Content.Application.Comments;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Threads;

namespace Forum.Modules.Content.Application.Caching;

/// <summary>
/// The Content module's read cache — exactly the three reads of <c>docs/specs/READ-CACHE-CONTRACT.md</c> §2 (C1 feed
/// page, C2 comment tree, C3 search page) and the four invalidation rows of its §3. Query handlers call it AFTER the
/// A-12 read gate, so a private category's cached page is never handed to a reader the gate rejected; nothing else
/// (thread detail, categories, reaction counts, profiles, social) is ever cached. Every cached object is identical
/// for all readers inside its key: C1/C2 are viewer-independent, C3 carries the viewer scope in the key.
/// Invalidation is local to this replica and runs right after the write is durable (§3).
/// </summary>
internal interface IContentReadCache
{
    /// <summary>False = <c>ReadCache:Enabled=false</c>: the reads below go straight to the store (no lookup).</summary>
    bool Enabled { get; }

    /// <summary>C1: one feed page of a category, keyed by (category id, sort, cursor, limit).</summary>
    ValueTask<CursorPage<ThreadFeedItemResponse>> GetFeedPageAsync(
        Ulid categoryId, ThreadFeedCursor? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>C2: the whole comment tree of a thread, keyed by thread id.</summary>
    ValueTask<IReadOnlyList<CommentResponse>> GetCommentTreeAsync(Ulid threadId, CancellationToken cancellationToken);

    /// <summary>
    /// C3: one search page, keyed by (viewer scope, normalized query, sort, cursor, limit). The readable private
    /// categories are the reader's own (computed by the handler's gate before the lookup) and only feed the store
    /// query on a miss; the scope in the key keeps one reader's page away from every other reader.
    /// </summary>
    ValueTask<CursorPage<ThreadFeedItemResponse>> SearchAsync(
        string viewerScope,
        string query,
        ThreadSearchSort sort,
        ThreadSearchCursor? cursor,
        int limit,
        IReadOnlyCollection<Ulid> readablePrivateCategoryIds,
        CancellationToken cancellationToken);

    /// <summary>§3: thread create, update, delete, pin, move → C1 all, C2 all, C3 all.</summary>
    ValueTask ThreadChangedAsync();

    /// <summary>§3: comment create, update → C2 of that thread only.</summary>
    ValueTask CommentWrittenAsync(Ulid threadId);

    /// <summary>§3: comment delete → C1 all, C2 all (search is untouched).</summary>
    ValueTask CommentDeletedAsync();

    /// <summary>
    /// §3: space (category) update, delete → C1 all, C2 all, C3 all (a visibility change moves every thread of the
    /// category into or out of readers' scopes). Create flushes nothing: a new category is empty and the category
    /// list is never cached.
    /// </summary>
    ValueTask CategoryChangedAsync();
}

/// <summary>
/// The stored shape of every cache entry. <c>HybridCache</c> hands its in-process L1 the stored instance back (no
/// per-hit serialization) only for types it can prove immutable: a <b>sealed</b> class carrying
/// <see cref="ImmutableObjectAttribute"/> (<c>ImmutableTypeCache.IsTypeImmutable</c> in the 10.2.0 source). The
/// payloads are immutable records already; this wrapper carries the proof.
/// </summary>
[ImmutableObject(true)]
internal sealed class ReadCacheEntry<T>
{
    public ReadCacheEntry(T value) => Value = value;

    public T Value { get; }
}

/// <summary>
/// The viewer scope of a C3 key: <c>anonymous</c>, or the signed-in reader's user id together with the private
/// categories that reader may open right now. Carrying the readable set in the scope is A's equivalent of B's
/// search flush on space join/leave (Hubert's 2026-11-06 audit): the moment an owner/moderator grant changes, the
/// reader's key changes with it, so a revoked member misses instead of being served the page computed under the
/// old access for up to one TTL. Anonymous readers (the measured path) share one scope.
/// </summary>
internal static class ReadCacheScope
{
    public const string Anonymous = "anonymous";

    public static string Of(Ulid? userId, IReadOnlyCollection<Ulid> readablePrivateCategoryIds)
    {
        if (userId is not { } id)
        {
            return Anonymous;
        }

        if (readablePrivateCategoryIds.Count == 0)
        {
            return id.ToString();
        }

        var readable = readablePrivateCategoryIds.Select(static category => category.ToString()).Order(StringComparer.Ordinal);
        return $"{id}+{string.Join('+', readable)}";
    }
}
