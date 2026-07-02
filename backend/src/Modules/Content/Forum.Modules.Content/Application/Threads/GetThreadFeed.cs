using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Domain.Categories;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// The category feed: pinned first, then newest, keyset-paged along <c>ix_threads_feed</c>. The cursor is the
/// opaque resume point issued by the previous page.
/// </summary>
internal sealed record GetThreadFeedQuery(Ulid CategoryId, string? Cursor, int Limit)
    : IQuery<CursorPage<ThreadFeedItemResponse>>;

internal sealed class GetThreadFeedQueryHandler : IQueryHandler<GetThreadFeedQuery, CursorPage<ThreadFeedItemResponse>>
{
    internal const int MaxLimit = 50;

    private readonly IContentQueries _queries;

    public GetThreadFeedQueryHandler(IContentQueries queries) => _queries = queries;

    public async Task<Result<CursorPage<ThreadFeedItemResponse>>> Handle(
        GetThreadFeedQuery query, CancellationToken cancellationToken)
    {
        if (!await _queries.CategoryExistsAsync(query.CategoryId, cancellationToken))
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
        return Result.Success(await _queries.GetThreadFeedAsync(query.CategoryId, cursor, limit, cancellationToken));
    }
}
