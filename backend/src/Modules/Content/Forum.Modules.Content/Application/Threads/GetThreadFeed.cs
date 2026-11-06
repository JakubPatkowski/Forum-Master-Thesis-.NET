using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Domain.Categories;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// The category feed: pinned first, then newest, keyset-paged along <c>ix_threads_feed</c>. The cursor is the
/// opaque resume point issued by the previous page. The page is the contract's C1 read cache entry
/// (<c>docs/specs/READ-CACHE-CONTRACT.md</c>), consulted only after the read gate passed.
/// </summary>
internal sealed record GetThreadFeedQuery(Ulid CategoryId, string? Cursor, int Limit)
    : IQuery<CursorPage<ThreadFeedItemResponse>>;

internal sealed class GetThreadFeedQueryHandler : IQueryHandler<GetThreadFeedQuery, CursorPage<ThreadFeedItemResponse>>
{
    internal const int MaxLimit = 50;

    private readonly IContentQueries _queries;
    private readonly ContentReadAccess _readAccess;
    private readonly IContentReadCache _readCache;

    public GetThreadFeedQueryHandler(IContentQueries queries, ContentReadAccess readAccess, IContentReadCache readCache)
    {
        _queries = queries;
        _readAccess = readAccess;
        _readCache = readCache;
    }

    public async Task<Result<CursorPage<ThreadFeedItemResponse>>> Handle(
        GetThreadFeedQuery query, CancellationToken cancellationToken)
    {
        // A private category the reader cannot open is indistinguishable from a missing one (A-12). The gate runs
        // on EVERY request — a cache hit never skips it (contract §2).
        var category = await _queries.GetCategoryReadInfoAsync(query.CategoryId, cancellationToken);
        if (category is null || !await _readAccess.CanReadAsync(category, cancellationToken))
        {
            return Result.Failure<CursorPage<ThreadFeedItemResponse>>(CategoryErrors.NotFound);
        }

        ThreadFeedCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            cursor = ThreadFeedCursor.TryDecode(query.Cursor);
            if (cursor is null)
            {
                return Result.Failure<CursorPage<ThreadFeedItemResponse>>(ContentErrors.InvalidCursor);
            }
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        return Result.Success(await _readCache.GetFeedPageAsync(query.CategoryId, cursor, limit, cancellationToken));
    }
}
