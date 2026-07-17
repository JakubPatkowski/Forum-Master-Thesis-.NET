using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Domain.Friendships;

internal static class FriendshipErrors
{
    public static readonly Error SelfRequest =
        Error.Validation("Friendship.SelfRequest", "You cannot send a friend request to yourself.");

    public static readonly Error NotAddressee =
        Error.Forbidden("Friendship.NotAddressee", "Only the addressee can accept a friend request.");

    public static readonly Error NotPending =
        Error.Validation("Friendship.NotPending", "This friend request is not pending.");
}
