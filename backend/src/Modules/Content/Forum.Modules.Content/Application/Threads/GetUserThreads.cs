using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Paging;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// A user's live threads, newest first (profile activity). An unknown owner id simply yields an
/// empty page — reads are pure filters, never existence checks (the Engagement batch precedent).
/// Threads in private categories the reader cannot open are omitted (A-12).
/// </summary>
internal sealed record GetUserThreadsQuery(Ulid OwnerId, string? Cursor, int Limit)
    : IQuery<CursorPage<ThreadFeedItemResponse>>;

internal sealed class GetUserThreadsQueryHandler : IQueryHandler<GetUserThreadsQuery, CursorPage<ThreadFeedItemResponse>>
{
    internal const int MaxLimit = 50;

    private readonly IContentQueries _queries;
    private readonly ContentReadAccess _readAccess;

    public GetUserThreadsQueryHandler(IContentQueries queries, ContentReadAccess readAccess)
    {
        _queries = queries;
        _readAccess = readAccess;
    }

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
        var readablePrivate = await _readAccess.ReadablePrivateCategoryIdsAsync(cancellationToken);
        return Result.Success(
            await _queries.GetThreadsByOwnerAsync(query.OwnerId, cursor, limit, readablePrivate, cancellationToken));
    }
}
