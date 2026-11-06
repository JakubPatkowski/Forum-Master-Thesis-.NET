using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Application.Paging;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// Full-text search over threads (tsvector + GIN, title weighted over body), keyset-paged in one of two orders
/// (<see cref="ThreadSearchSort"/>). Only threads in categories the reader may open are searched (A-12); the
/// reader's readable set is resolved BEFORE the cache lookup and the page is the contract's C3 entry, keyed by the
/// viewer scope (<c>docs/specs/READ-CACHE-CONTRACT.md</c>; the scope carries the readable set, see
/// <see cref="ReadCacheScope"/>). The query enters the key trimmed and otherwise verbatim (case-sensitive) — the
/// same normalization B applies, agreed after Hubert's 2026-11-06 audit.
/// </summary>
internal sealed record SearchThreadsQuery(string? Q, ThreadSearchSort Sort, string? Cursor, int Limit)
    : IQuery<CursorPage<ThreadFeedItemResponse>>;

internal sealed class SearchThreadsQueryHandler : IQueryHandler<SearchThreadsQuery, CursorPage<ThreadFeedItemResponse>>
{
    private readonly ContentReadAccess _readAccess;
    private readonly IContentReadCache _readCache;
    private readonly ICurrentUser _currentUser;

    public SearchThreadsQueryHandler(ContentReadAccess readAccess, IContentReadCache readCache, ICurrentUser currentUser)
    {
        _readAccess = readAccess;
        _readCache = readCache;
        _currentUser = currentUser;
    }

    public async Task<Result<CursorPage<ThreadFeedItemResponse>>> Handle(
        SearchThreadsQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Q))
        {
            return Result.Failure<CursorPage<ThreadFeedItemResponse>>(ContentErrors.EmptySearchQuery);
        }

        ThreadSearchCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            cursor = ThreadSearchCursor.TryDecode(query.Cursor);
            if (cursor is null)
            {
                return Result.Failure<CursorPage<ThreadFeedItemResponse>>(ContentErrors.InvalidCursor);
            }

            // A relevance resume point means nothing in the newest order (and vice versa): refuse, never reinterpret.
            if (cursor.Sort != query.Sort)
            {
                return Result.Failure<CursorPage<ThreadFeedItemResponse>>(ContentErrors.CursorSortMismatch);
            }
        }

        var limit = Math.Clamp(query.Limit, 1, GetThreadFeedQueryHandler.MaxLimit);
        var readablePrivate = await _readAccess.ReadablePrivateCategoryIdsAsync(cancellationToken);
        var scope = ReadCacheScope.Of(_currentUser.IsAuthenticated ? _currentUser.Id : null, readablePrivate);
        return Result.Success(await _readCache.SearchAsync(
            scope, query.Q.Trim(), query.Sort, cursor, limit, readablePrivate, cancellationToken));
    }
}
