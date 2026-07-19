using Forum.Modules.Social.Domain.Conversations;
using Forum.Modules.Social.Domain.Friendships;
using Forum.Modules.Social.Domain.Groups;
using Forum.Modules.Social.Domain.Notifications;
using Forum.Modules.Social.Domain.Privacy;

namespace Forum.Modules.Social.Application.Abstractions;

/// <summary>Write-model access for friendships (one row per pair, either direction).</summary>
internal interface IFriendshipRepository
{
    Task<Friendship?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    /// <summary>The pair's row regardless of who requested whom, or null.</summary>
    Task<Friendship?> GetBetweenAsync(Ulid userA, Ulid userB, CancellationToken cancellationToken);

    void Add(Friendship friendship);

    void Remove(Friendship friendship);

    /// <summary>Bulk-removes every pending request the user is on either side of (admin-ban cleanup).</summary>
    Task RemovePendingInvolvingAsync(Ulid userId, CancellationToken cancellationToken);
}

/// <summary>Write-model access for peer blocks.</summary>
internal interface ISocialBlockRepository
{
    Task<SocialBlock?> GetAsync(Ulid blockerId, Ulid blockedId, CancellationToken cancellationToken);

    /// <summary>True when either user blocks the other — the gate every social interaction checks.</summary>
    Task<bool> AnyBetweenAsync(Ulid userA, Ulid userB, CancellationToken cancellationToken);

    void Add(SocialBlock block);

    void Remove(SocialBlock block);
}

/// <summary>Write-model access for groups and their membership facts.</summary>
internal interface IGroupRepository
{
    Task<Group?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    void Add(Group group);

    Task<GroupMembership?> GetMembershipAsync(Ulid groupId, Ulid userId, CancellationToken cancellationToken);

    void AddMembership(GroupMembership membership);

    void RemoveMembership(GroupMembership membership);
}

/// <summary>Write-model access for pending group invites (non-pending rows are deleted, never kept).</summary>
internal interface IGroupInviteRepository
{
    Task<GroupInvite?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    Task<GroupInvite?> GetPendingAsync(Ulid groupId, Ulid invitedUserId, CancellationToken cancellationToken);

    void Add(GroupInvite invite);

    void Remove(GroupInvite invite);

    /// <summary>Bulk-removes pending invites between the pair in both roles (block cleanup).</summary>
    Task RemoveBetweenAsync(Ulid userA, Ulid userB, CancellationToken cancellationToken);

    /// <summary>Bulk-removes every pending invite the user is on either side of (admin-ban cleanup).</summary>
    Task RemoveInvolvingAsync(Ulid userId, CancellationToken cancellationToken);
}

/// <summary>Write-model access for conversations, participants and messages.</summary>
internal interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    Task<Conversation?> GetDirectByKeyAsync(string directKey, CancellationToken cancellationToken);

    void Add(Conversation conversation);

    Task<ConversationParticipant?> GetParticipantAsync(
        Ulid conversationId, Ulid userId, CancellationToken cancellationToken);

    /// <summary>The active (non-left) participants — two for a DM, the member set for a group chat.</summary>
    Task<IReadOnlyList<ConversationParticipant>> GetActiveParticipantsAsync(
        Ulid conversationId, CancellationToken cancellationToken);

    void AddParticipant(ConversationParticipant participant);

    Task<Message?> GetMessageAsync(Ulid messageId, CancellationToken cancellationToken);

    void AddMessage(Message message);
}

/// <summary>Write-model access for durable notifications.</summary>
internal interface INotificationRepository
{
    void Add(Notification notification);

    /// <summary>Marks the given ids (or ALL unread when null) read for the user; returns the affected count.</summary>
    Task<int> MarkReadAsync(Ulid userId, IReadOnlyList<Ulid>? ids, CancellationToken cancellationToken);
}

/// <summary>Write-model access for privacy settings (absent row = defaults).</summary>
internal interface IPrivacySettingsRepository
{
    Task<UserPrivacySettings?> GetAsync(Ulid userId, CancellationToken cancellationToken);

    void Add(UserPrivacySettings settings);
}

/// <summary>Reads the target user's existence/liveness from forum_identity (ADO read, Engagement's
/// ContentTargetReader precedent — later modules may read earlier modules' tables, never write them).</summary>
internal interface IUserReader
{
    /// <summary>True when the user exists and is active — banned/pending accounts are socially invisible.</summary>
    Task<bool> IsActiveAsync(Ulid userId, CancellationToken cancellationToken);
}

/// <summary>The module's unit of work (save + domain-event dispatch).</summary>
internal interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
