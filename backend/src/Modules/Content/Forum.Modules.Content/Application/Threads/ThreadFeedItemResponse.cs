namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// One row of <c>forum_content.thread_feed_v</c> plus a 300-character plain-text <see cref="Excerpt"/> of the body
/// (A/B parity with Architecture B's card previews, deviation D2). Counts are placeholders until Phase 4 (Engagement).
/// </summary>
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
    DateTimeOffset? LastModifiedOnUtc,
    string Excerpt);
