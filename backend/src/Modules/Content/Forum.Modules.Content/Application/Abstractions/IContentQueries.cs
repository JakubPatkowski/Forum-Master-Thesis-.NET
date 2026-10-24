using Forum.Common.Paging;
using Forum.Modules.Content.Application.Categories;
using Forum.Modules.Content.Application.Comments;
using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Tags;
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

    /// <summary>The read-gate facts of a live category (owner + private flag), or null when it does not exist.</summary>
    Task<CategoryReadInfo?> GetCategoryReadInfoAsync(Ulid categoryId, CancellationToken cancellationToken);

    /// <summary>The read-gate facts of a live thread's category, or null when the thread does not exist.</summary>
    Task<CategoryReadInfo?> GetThreadCategoryReadInfoAsync(Ulid threadId, CancellationToken cancellationToken);

    /// <summary>Every live private category (the candidates an authenticated reader may be granted).</summary>
    Task<IReadOnlyList<CategoryReadInfo>> ListPrivateCategoriesAsync(CancellationToken cancellationToken);

    Task<CursorPage<ThreadFeedItemResponse>> GetThreadFeedAsync(
        Ulid categoryId, ThreadFeedCursor? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Ranked FTS over threads in public categories plus <paramref name="readablePrivateCategoryIds"/> (A-12: a
    /// private category's threads never surface to a reader who cannot open the category).
    /// </summary>
    Task<CursorPage<ThreadFeedItemResponse>> SearchThreadsAsync(
        string query,
        ThreadSearchCursor? cursor,
        int limit,
        IReadOnlyCollection<Ulid> readablePrivateCategoryIds,
        CancellationToken cancellationToken);

    /// <summary>The thread detail together with its category's read-gate facts (one round-trip).</summary>
    Task<ThreadDetailRead?> GetThreadAsync(Ulid threadId, CancellationToken cancellationToken);

    /// <summary>The full tree of a thread in <c>ORDER BY path</c> (depth-first) order, deleted rows included.</summary>
    Task<IReadOnlyList<CommentResponse>> GetCommentTreeAsync(Ulid threadId, CancellationToken cancellationToken);

    /// <summary>Tags ranked by live-thread usage; optional lower-case substring filter on slug.</summary>
    Task<IReadOnlyList<TagSuggestionResponse>> SuggestTagsAsync(
        string? slugFilter, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// A user's live threads in categories the reader may open, newest first (keyset). Unknown owner → empty page.
    /// </summary>
    Task<CursorPage<ThreadFeedItemResponse>> GetThreadsByOwnerAsync(
        Ulid ownerId,
        OwnerActivityCursor? cursor,
        int limit,
        IReadOnlyCollection<Ulid> readablePrivateCategoryIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// A user's live comments on live threads in categories the reader may open, newest first (keyset). Unknown
    /// owner → empty page.
    /// </summary>
    Task<CursorPage<CommentActivityItemResponse>> GetCommentsByOwnerAsync(
        Ulid ownerId,
        OwnerActivityCursor? cursor,
        int limit,
        IReadOnlyCollection<Ulid> readablePrivateCategoryIds,
        CancellationToken cancellationToken);
}
