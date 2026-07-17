using Forum.Common.Paging;

namespace Forum.Modules.Social.Application.Abstractions;

// ---- Read DTOs (raw-ADO view rows; usernames come from the views' forum_identity read joins) ----

internal sealed record FriendResponse(Ulid FriendshipId, Ulid UserId, string Username, DateTimeOffset FriendsSinceUtc);

internal sealed record FriendRequestResponse(
    Ulid FriendshipId, Ulid RequesterId, string RequesterUsername, Ulid AddresseeId, string AddresseeUsername,
    DateTimeOffset SentOnUtc);

internal sealed record FriendRequestsResponse(
    IReadOnlyList<FriendRequestResponse> Incoming, IReadOnlyList<FriendRequestResponse> Outgoing);

internal sealed record BlockedUserResponse(Ulid UserId, string Username, DateTimeOffset BlockedOnUtc);

internal sealed record GroupSummaryResponse(
    Ulid GroupId, string Name, string Description, string Visibility, Ulid OwnerId, string OwnerUsername,
    int MemberCount, bool IsMember, DateTimeOffset CreatedOnUtc);

internal sealed record GroupDetailResponse(
    Ulid GroupId, string Name, string Description, string Visibility, Ulid OwnerId, string OwnerUsername,
    int MemberCount, bool IsMember, bool IsAdmin, DateTimeOffset CreatedOnUtc);

internal sealed record GroupMemberResponse(
    Ulid UserId, string Username, DateTimeOffset JoinedOnUtc, bool IsOwner, bool IsAdmin);

internal sealed record GroupInviteResponse(
    Ulid InviteId, Ulid GroupId, string GroupName, Ulid InvitedUserId, string InvitedUserUsername,
    Ulid InvitedBy, string InvitedByUsername, DateTimeOffset SentOnUtc);

internal sealed record ConversationResponse(
    Ulid ConversationId, string Type, string DisplayName, Ulid? OtherUserId, Ulid? GroupId,
    Ulid? LastMessageId, string? LastMessagePreview, Ulid? LastMessageSenderId, DateTimeOffset? LastMessageOnUtc,
    int UnreadCount, bool IsMuted);

internal sealed record MessageResponse(
    Ulid MessageId, Ulid ConversationId, Ulid SenderId, string SenderUsername, string Body,
    DateTimeOffset SentOnUtc, DateTimeOffset? EditedOnUtc, bool IsDeleted);

internal sealed record NotificationResponse(
    Ulid NotificationId, string Kind, Ulid? ActorId, string? ActorUsername, Ulid? TargetId, bool IsRead,
    DateTimeOffset CreatedOnUtc);

/// <summary>Which groups a directory listing covers.</summary>
internal enum GroupListFilter
{
    /// <summary>Public groups plus the viewer's own memberships.</summary>
    All,

    /// <summary>Only groups the viewer is a member of.</summary>
    Mine,

    /// <summary>Only public groups.</summary>
    Public,
}

/// <summary>
/// The module's raw-ADO read side over the <c>forum_social</c> views (ContentQueries precedent): keyset
/// pagination everywhere a list can grow without bound (cursor = the last row's ULID — descending id order is
/// descending creation order), never OFFSET. Conversation lists are the one deliberate exception: last-activity
/// ordering is unstable under keyset, and the list is naturally bounded, so it is hard-capped instead.
/// </summary>
internal interface ISocialQueries
{
    Task<CursorPage<FriendResponse>> GetFriendsAsync(
        Ulid userId, Ulid? cursor, int limit, CancellationToken cancellationToken);

    Task<FriendRequestsResponse> GetFriendRequestsAsync(Ulid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockedUserResponse>> GetBlockedUsersAsync(Ulid userId, CancellationToken cancellationToken);

    Task<CursorPage<GroupSummaryResponse>> GetGroupsAsync(
        Ulid viewerId, GroupListFilter filter, Ulid? cursor, int limit, CancellationToken cancellationToken);

    Task<GroupDetailResponse?> GetGroupAsync(Ulid groupId, Ulid viewerId, CancellationToken cancellationToken);

    Task<CursorPage<GroupMemberResponse>> GetGroupMembersAsync(
        Ulid groupId, Ulid? cursor, int limit, CancellationToken cancellationToken);

    /// <summary>Pending invites addressed TO the user.</summary>
    Task<IReadOnlyList<GroupInviteResponse>> GetMyInvitesAsync(Ulid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationResponse>> GetConversationsAsync(
        Ulid userId, int limit, CancellationToken cancellationToken);

    /// <summary>Newest-first keyset history; includes tombstoned rows with the body already masked by the view.</summary>
    Task<CursorPage<MessageResponse>> GetMessagesAsync(
        Ulid conversationId, Ulid? cursor, int limit, CancellationToken cancellationToken);

    Task<CursorPage<NotificationResponse>> GetNotificationsAsync(
        Ulid userId, bool unreadOnly, Ulid? cursor, int limit, CancellationToken cancellationToken);

    Task<int> GetUnreadNotificationCountAsync(Ulid userId, CancellationToken cancellationToken);
}
