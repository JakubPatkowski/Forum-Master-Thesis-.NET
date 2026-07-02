using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Comments;

/// <summary>
/// The full comment tree of a thread in materialized-path (depth-first) order. Soft-deleted comments are kept
/// with their <c>"[deleted]"</c> body so replies stay anchored.
/// </summary>
internal sealed record GetCommentTreeQuery(Ulid ThreadId) : IQuery<IReadOnlyList<CommentResponse>>;

internal sealed class GetCommentTreeQueryHandler : IQueryHandler<GetCommentTreeQuery, IReadOnlyList<CommentResponse>>
{
    private readonly IContentQueries _queries;

    public GetCommentTreeQueryHandler(IContentQueries queries) => _queries = queries;

    public async Task<Result<IReadOnlyList<CommentResponse>>> Handle(
        GetCommentTreeQuery query, CancellationToken cancellationToken)
    {
        if (!await _queries.ThreadExistsAsync(query.ThreadId, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<CommentResponse>>(ThreadErrors.NotFound);
        }

        return Result.Success(await _queries.GetCommentTreeAsync(query.ThreadId, cancellationToken));
    }
}
