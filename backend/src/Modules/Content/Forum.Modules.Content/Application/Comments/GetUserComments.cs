using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Paging;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Comments;

/// <summary>
/// A user's live comments on live threads, newest first (profile activity). Unknown owner id
/// yields an empty page — reads are pure filters, never existence checks. Comments on threads in
/// private categories the reader cannot open are omitted (A-12).
/// </summary>
internal sealed record GetUserCommentsQuery(Ulid OwnerId, string? Cursor, int Limit)
    : IQuery<CursorPage<CommentActivityItemResponse>>;

internal sealed class GetUserCommentsQueryHandler
    : IQueryHandler<GetUserCommentsQuery, CursorPage<CommentActivityItemResponse>>
{
    internal const int MaxLimit = 50;

    private readonly IContentQueries _queries;
    private readonly ContentReadAccess _readAccess;

    public GetUserCommentsQueryHandler(IContentQueries queries, ContentReadAccess readAccess)
    {
        _queries = queries;
        _readAccess = readAccess;
    }

    public async Task<Result<CursorPage<CommentActivityItemResponse>>> Handle(
        GetUserCommentsQuery query, CancellationToken cancellationToken)
    {
        OwnerActivityCursor? cursor = null;
        if (!string.IsNullOrWhiteSpace(query.Cursor))
        {
            cursor = OwnerActivityCursor.TryDecode(query.Cursor);
            if (cursor is null)
            {
                return Result.Failure<CursorPage<CommentActivityItemResponse>>(ContentErrors.InvalidCursor);
            }
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        var readablePrivate = await _readAccess.ReadablePrivateCategoryIdsAsync(cancellationToken);
        return Result.Success(
            await _queries.GetCommentsByOwnerAsync(query.OwnerId, cursor, limit, readablePrivate, cancellationToken));
    }
}
