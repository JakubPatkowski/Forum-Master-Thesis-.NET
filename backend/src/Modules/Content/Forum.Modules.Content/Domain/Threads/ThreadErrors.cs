using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Domain.Threads;

/// <summary>Typed errors for the thread lifecycle. No exceptions for expected failures.</summary>
internal static class ThreadErrors
{
    public static readonly Error NotFound = Error.NotFound("thread.not_found", "Thread not found.");
    public static readonly Error AlreadyDeleted = Error.Conflict("thread.already_deleted", "Thread is already deleted.");
    public static readonly Error NotOwnerNorModerator = Error.Forbidden(
        "thread.forbidden", "Only the author or a moderator may modify this thread.");
    public static readonly Error CreateForbidden = Error.Forbidden(
        "thread.create_forbidden", "Missing the create permission for this category.");
    public static readonly Error ModerationRequired = Error.Forbidden(
        "thread.moderation_required", "This action requires the moderate permission.");
    public static readonly Error SameCategory = Error.Validation(
        "thread.same_category", "The thread is already in this category.");
}
