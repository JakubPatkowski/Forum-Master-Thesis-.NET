using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Domain.Groups;

internal static class GroupErrors
{
    public static readonly Error AlreadyDeleted =
        Error.NotFound("Group.AlreadyDeleted", "The group no longer exists.");

    public static readonly Error OwnerCannotLeave = Error.Validation(
        "Group.OwnerCannotLeave", "The owner cannot leave their group — transfer ownership or delete it instead.");

    public static readonly Error OwnerCannotBeKicked =
        Error.Validation("Group.OwnerCannotBeKicked", "The group owner cannot be removed from the group.");
}
