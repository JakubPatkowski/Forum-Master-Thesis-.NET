using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Paging;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Comments;

/// <summary>
/// A user's live comments on live threads, newest first (profile activity). Unknown owner id
/// yields an empty page — reads are pure filters, never existence checks.
/// </summary>
internal sealed record GetUserCommentsQuery(Ulid OwnerId, string? Cursor, int Limit)
    : IQuery<CursorPage<CommentActivityItemResponse>>;

internal sealed class GetUserCommentsQueryHandler
    : IQueryHandler<GetUserCommentsQuery, CursorPage<CommentActivityItemResponse>>
{
    internal const int MaxLimit = 50;

    private readonly IContentQueries _queries;

    public GetUserCommentsQueryHandler(IContentQueries queries) => _queries = queries;

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
        return Result.Success(
            await _queries.GetCommentsByOwnerAsync(query.OwnerId, cursor, limit, cancellationToken));
    }
}
