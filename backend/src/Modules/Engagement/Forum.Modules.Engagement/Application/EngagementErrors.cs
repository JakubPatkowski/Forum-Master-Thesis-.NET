using Forum.SharedKernel.Results;

namespace Forum.Modules.Engagement.Application;

/// <summary>Typed errors of the Engagement application layer. No exceptions for expected failures.</summary>
internal static class EngagementErrors
{
    /// <summary>Upper bound on ids per batch summary request (two feed pages' worth, comfortably).</summary>
    public const int MaxBatchTargets = 100;

    public static readonly Error AuthenticationRequired =
        Error.Unauthorized("auth.required", "Authentication required.");

    public static readonly Error TargetNotFound =
        Error.NotFound("reaction.target_not_found", "The content to react to does not exist.");

    public static readonly Error PrivateCategory = Error.Forbidden(
        "reaction.private_category", "Only the owner or moderators may react to content in a private category.");

    public static readonly Error LikeForbidden =
        Error.Forbidden("reaction.forbidden", "You may not react to content in this category.");

    public static readonly Error InvalidTargetType =
        Error.Validation("reaction.invalid_target_type", "The target type must be 'thread' or 'comment'.");

    public static readonly Error InvalidTargetId =
        Error.Validation("reaction.invalid_target_id", "One or more target ids are not valid ULIDs.");

    public static readonly Error NoTargets =
        Error.Validation("reaction.no_targets", "At least one target id is required.");

    public static readonly Error TooManyTargets = Error.Validation(
        "reaction.too_many_targets", $"At most {MaxBatchTargets} target ids may be queried at once.");

    public static readonly Error UserNotFound =
        Error.NotFound("user.not_found", "User not found.");
}
