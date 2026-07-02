using Forum.Common.Paging;
using Forum.Modules.Content.Application.Categories;
using Forum.Modules.Content.Application.Comments;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Threads;

namespace Forum.Modules.Content.Application.Abstractions;

/// <summary>
/// The Content read side: SQL views + keyset pagination (never OFFSET). Implementations read
/// <c>thread_feed_v</c> / <c>thread_detail_v</c> / <c>comment_tree_v</c>, which already resolve the author join
/// and (for feeds) exclude soft-deleted rows.
/// </summary>
internal interface IContentQueries
{
    Task<IReadOnlyList<CategoryResponse>> ListCategoriesAsync(CancellationToken cancellationToken);

    Task<CategoryResponse?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<bool> CategoryExistsAsync(Ulid categoryId, CancellationToken cancellationToken);

    Task<bool> ThreadExistsAsync(Ulid threadId, CancellationToken cancellationToken);

    Task<CursorPage<ThreadFeedItemResponse>> GetThreadFeedAsync(
        Ulid categoryId, ThreadFeedCursor? cursor, int limit, CancellationToken cancellationToken);

    Task<CursorPage<ThreadFeedItemResponse>> SearchThreadsAsync(
        string query, ThreadSearchCursor? cursor, int limit, CancellationToken cancellationToken);

    Task<ThreadDetailResponse?> GetThreadAsync(Ulid threadId, CancellationToken cancellationToken);

    /// <summary>The full tree of a thread in <c>ORDER BY path</c> (depth-first) order, deleted rows included.</summary>
    Task<IReadOnlyList<CommentResponse>> GetCommentTreeAsync(Ulid threadId, CancellationToken cancellationToken);
}
