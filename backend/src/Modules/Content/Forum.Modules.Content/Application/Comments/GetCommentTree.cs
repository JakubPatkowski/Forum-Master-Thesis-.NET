using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Comments;

/// <summary>
/// The full comment tree of a thread in materialized-path (depth-first) order. Soft-deleted comments are kept
/// with their <c>"[deleted]"</c> body so replies stay anchored. The tree is the contract's C2 read cache entry
/// (<c>docs/specs/READ-CACHE-CONTRACT.md</c>), consulted only after the read gate passed.
/// </summary>
internal sealed record GetCommentTreeQuery(Ulid ThreadId) : IQuery<IReadOnlyList<CommentResponse>>;

internal sealed class GetCommentTreeQueryHandler : IQueryHandler<GetCommentTreeQuery, IReadOnlyList<CommentResponse>>
{
    private readonly IContentQueries _queries;
    private readonly ContentReadAccess _readAccess;
    private readonly IContentReadCache _readCache;

    public GetCommentTreeQueryHandler(IContentQueries queries, ContentReadAccess readAccess, IContentReadCache readCache)
    {
        _queries = queries;
        _readAccess = readAccess;
        _readCache = readCache;
    }

    public async Task<Result<IReadOnlyList<CommentResponse>>> Handle(
        GetCommentTreeQuery query, CancellationToken cancellationToken)
    {
        // Gate first, cache second (contract §2): a hit never skips the owner-or-moderator check of a private category.
        var category = await _queries.GetThreadCategoryReadInfoAsync(query.ThreadId, cancellationToken);
        if (category is null || !await _readAccess.CanReadAsync(category, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<CommentResponse>>(ThreadErrors.NotFound);
        }

        return Result.Success(await _readCache.GetCommentTreeAsync(query.ThreadId, cancellationToken));
    }
}
