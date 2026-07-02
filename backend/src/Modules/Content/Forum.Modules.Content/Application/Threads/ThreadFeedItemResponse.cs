namespace Forum.Modules.Content.Application.Threads;

/// <summary>One row of <c>forum_content.thread_feed_v</c>. Counts are placeholders until Phase 4 (Engagement).</summary>
internal sealed record ThreadFeedItemResponse(
    Ulid Id,
    Ulid CategoryId,
    string CategorySlug,
    string CategoryName,
    string Title,
    bool IsPinned,
    Ulid OwnerId,
    string Username,
    string DisplayName,
    int LikeCount,
    int CommentCount,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset? LastModifiedOnUtc);
