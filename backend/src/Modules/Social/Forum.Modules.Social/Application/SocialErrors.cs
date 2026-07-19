using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application;

/// <summary>
/// The module's expected-failure catalog, in the shared 404 → 403 → 422 vocabulary. Block- and privacy-based
/// denials share ONE deliberately generic Forbidden error each per interaction kind, so a peer block is
/// indistinguishable from a privacy setting on the wire (revealing "you were blocked" invites harassment).
/// </summary>
internal static class SocialErrors
{
    public static readonly Error AuthenticationRequired =
        Error.Unauthorized("Social.AuthenticationRequired", "Authentication is required.");

    public static readonly Error UserNotFound =
        Error.NotFound("Social.UserNotFound", "The user does not exist.");

    public static readonly Error InvalidCursor =
        Error.Validation("Social.InvalidCursor", "The paging cursor is malformed.");

    // Friends
    public static readonly Error FriendRequestNotAllowed = Error.Forbidden(
        "Social.FriendRequestNotAllowed", "You cannot send a friend request to this user.");

    public static readonly Error FriendshipNotFound =
        Error.NotFound("Social.FriendshipNotFound", "No such friend request or friendship.");

    public static readonly Error AlreadyFriends =
        Error.Conflict("Social.AlreadyFriends", "You are already friends with this user.");

    public static readonly Error RequestAlreadyPending = Error.Conflict(
        "Social.RequestAlreadyPending", "A friend request between you and this user is already pending.");

    public static readonly Error NotAFriendRequest = Error.Validation(
        "Social.NotAFriendRequest", "This friendship is not a pending request — remove the friend instead.");

    public static readonly Error NotFriends =
        Error.NotFound("Social.NotFriends", "You are not friends with this user.");

    // Groups
    public static readonly Error GroupNotFound =
        Error.NotFound("Social.GroupNotFound", "The group does not exist.");

    public static readonly Error GroupForbidden =
        Error.Forbidden("Social.GroupForbidden", "You do not have permission to manage this group.");

    public static readonly Error GroupPrivate =
        Error.Forbidden("Social.GroupPrivate", "This group is private — you need an invitation.");

    public static readonly Error NotGroupMember =
        Error.Forbidden("Social.NotGroupMember", "Only group members can do this.");

    public static readonly Error MembershipNotFound =
        Error.NotFound("Social.MembershipNotFound", "The user is not a member of this group.");

    public static readonly Error AlreadyGroupMember =
        Error.Conflict("Social.AlreadyGroupMember", "The user is already a member of this group.");

    public static readonly Error InviteNotFound =
        Error.NotFound("Social.InviteNotFound", "The invitation does not exist.");

    public static readonly Error AlreadyInvited =
        Error.Conflict("Social.AlreadyInvited", "The user already has a pending invitation to this group.");

    public static readonly Error GroupInviteNotAllowed = Error.Forbidden(
        "Social.GroupInviteNotAllowed", "You cannot invite this user to a group.");

    public static readonly Error OwnerRoleImmutable = Error.Validation(
        "Social.OwnerRoleImmutable", "The owner's role cannot be changed — transfer ownership instead.");

    public static readonly Error TransferTargetNotMember = Error.Validation(
        "Social.TransferTargetNotMember", "Ownership can only be transferred to a current member.");

    public static readonly Error UnknownRole =
        Error.Validation("Social.UnknownRole", "Role must be 'admin' or 'member'.");

    public static readonly Error UnknownVisibility =
        Error.Validation("Social.UnknownVisibility", "Visibility must be 'public' or 'private'.");

    // Messaging
    public static readonly Error ConversationNotFound =
        Error.NotFound("Social.ConversationNotFound", "The conversation does not exist.");

    public static readonly Error NotParticipant =
        Error.Forbidden("Social.NotParticipant", "You are not a participant of this conversation.");

    public static readonly Error MessageNotFound =
        Error.NotFound("Social.MessageNotFound", "The message does not exist.");

    public static readonly Error NotMessageSender =
        Error.Forbidden("Social.NotMessageSender", "Only the sender can do this.");

    public static readonly Error MessageNotAllowed =
        Error.Forbidden("Social.MessageNotAllowed", "You cannot message this user.");

    public static readonly Error SelfConversation =
        Error.Validation("Social.SelfConversation", "You cannot open a conversation with yourself.");

    public static readonly Error SelfBlock =
        Error.Validation("Social.SelfBlock", "You cannot block yourself.");

    // Presence
    public static readonly Error TooManyPresenceIds = Error.Validation(
        "Social.TooManyPresenceIds", "At most 100 user ids per presence lookup.");

    public static readonly Error UnknownAudience = Error.Validation(
        "Social.UnknownAudience", "Audience must be 'everyone', 'friends' or 'no_one'.");
}
