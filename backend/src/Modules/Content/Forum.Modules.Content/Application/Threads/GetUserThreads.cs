using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Paging;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// A user's live threads, newest first (profile activity). An unknown owner id simply yields an
/// empty page — reads are pure filters, never existence checks (the Engagement batch precedent).
/// </summary>
internal sealed record GetUserThreadsQuery(Ulid OwnerId, string? Cursor, int Limit)
    : IQuery<CursorPage<ThreadFeedItemResponse>>;

internal sealed class GetUserThreadsQueryHandler : IQueryHandler<GetUserThreadsQuery, CursorPage<ThreadFeedItemResponse>>
{
    internal const int MaxLimit = 50;

    private readonly IContentQueries _queries;

    public GetUserThreadsQueryHandler(IContentQueries queries) => _queries = queries;

    public async Task<Result<CursorPage<ThreadFeedItemResponse>>> Handle(
        GetUserThreadsQuery query, CancellationToken cancellationToken)
    {
        OwnerActivityCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            cursor = OwnerActivityCursor.TryDecode(query.Cursor);
            if (cursor is null)
            {
                return Result.Failure<CursorPage<ThreadFeedItemResponse>>(ContentErrors.InvalidCursor);
            }
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        return Result.Success(
            await _queries.GetThreadsByOwnerAsync(query.OwnerId, cursor, limit, cancellationToken));
    }
}
