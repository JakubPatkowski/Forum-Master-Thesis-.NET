using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Domain.Comments;

/// <summary>Typed errors for the comment lifecycle. No exceptions for expected failures.</summary>
internal static class CommentErrors
{
    public static readonly Error NotFound = Error.NotFound("comment.not_found", "Comment not found.");
    public static readonly Error ParentNotFound = Error.NotFound("comment.parent_not_found", "Parent comment not found.");
    public static readonly Error AlreadyDeleted = Error.Conflict("comment.already_deleted", "Comment is already deleted.");
    public static readonly Error MaxDepthExceeded = Error.Validation(
        "comment.max_depth_exceeded", $"Comments may only nest {Comment.MaxDepth} levels deep.");
    public static readonly Error ParentInDifferentThread = Error.Validation(
        "comment.parent_in_different_thread", "The parent comment belongs to a different thread.");
    public static readonly Error NotOwnerNorModerator = Error.Forbidden(
        "comment.forbidden", "Only the author or a moderator may modify this comment.");
    public static readonly Error CommentForbidden = Error.Forbidden(
        "comment.create_forbidden", "Missing the comment permission for this category.");
}
