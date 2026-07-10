namespace Forum.Modules.Content.Application.Comments;

/// <summary>One row of a user's comment activity: the comment plus its live thread's title for context.</summary>
internal sealed record CommentActivityItemResponse(
    Ulid Id,
    Ulid ThreadId,
    string ThreadTitle,
    string Body,
    DateTimeOffset CreatedOnUtc);
