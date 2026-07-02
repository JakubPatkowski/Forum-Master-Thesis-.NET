using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Paging;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>Full-text search over threads (tsvector + GIN, title weighted over body), ranked and keyset-paged.</summary>
internal sealed record SearchThreadsQuery(string? Q, string? Cursor, int Limit)
    : IQuery<CursorPage<ThreadFeedItemResponse>>;

internal sealed class SearchThreadsQueryHandler : IQueryHandler<SearchThreadsQuery, CursorPage<ThreadFeedItemResponse>>
{
    private readonly IContentQueries _queries;

    public SearchThreadsQueryHandler(IContentQueries queries) => _queries = queries;

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
        }

        var limit = Math.Clamp(query.Limit, 1, GetThreadFeedQueryHandler.MaxLimit);
        return Result.Success(await _queries.SearchThreadsAsync(query.Q.Trim(), cursor, limit, cancellationToken));
    }
}
