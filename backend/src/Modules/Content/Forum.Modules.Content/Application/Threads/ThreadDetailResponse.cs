namespace Forum.Modules.Content.Application.Threads;

/// <summary>One row of <c>forum_content.thread_detail_v</c>: the full thread with author, category and tags.</summary>
internal sealed record ThreadDetailResponse(
    Ulid Id,
    Ulid CategoryId,
    string CategorySlug,
    string CategoryName,
    string Title,
    string Body,
    bool IsPinned,
    Ulid OwnerId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedOnUtc,
    DateTimeOffset? LastModifiedOnUtc);
