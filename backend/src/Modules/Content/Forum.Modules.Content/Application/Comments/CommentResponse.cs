namespace Forum.Modules.Content.Application.Comments;

/// <summary>
/// One row of <c>forum_content.comment_tree_v</c>. Soft-deleted comments stay in the tree with a
/// <c>"[deleted]"</c> body so their children keep a parent to hang from.
/// </summary>
internal sealed record CommentResponse(
    Ulid Id,
    Ulid ThreadId,
    Ulid? ParentId,
    string Path,
    int Depth,
    string Body,
    bool IsDeleted,
    Ulid OwnerId,
    string Username,
    string DisplayName,
    DateTimeOffset CreatedOnUtc);
