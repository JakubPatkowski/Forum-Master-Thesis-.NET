using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Conversations;
using Forum.Modules.Social.Domain.Friendships;
using Forum.Modules.Social.Domain.Groups;
using Forum.Modules.Social.Domain.Notifications;
using Forum.Modules.Social.Domain.Privacy;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Social.Infrastructure.Persistence;

// The module's EF write-model adapters. Reads that feed mutations are AsTracking (the base context defaults to
// NoTracking); bulk cleanups use ExecuteDelete so ban/block sweeps stay single statements.

internal sealed class FriendshipRepository : IFriendshipRepository
{
    private readonly SocialDbContext _db;

    public FriendshipRepository(SocialDbContext db) => _db = db;

    public Task<Friendship?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Friendships.AsTracking().FirstOrDefaultAsync(friendship => friendship.Id == id, cancellationToken);

    public Task<Friendship?> GetBetweenAsync(Ulid userA, Ulid userB, CancellationToken cancellationToken) =>
        _db.Friendships.AsTracking().FirstOrDefaultAsync(
            friendship =>
                (friendship.RequesterId == userA && friendship.AddresseeId == userB)
                || (friendship.RequesterId == userB && friendship.AddresseeId == userA),
            cancellationToken);

    public void Add(Friendship friendship) => _db.Friendships.Add(friendship);

    public void Remove(Friendship friendship) => _db.Friendships.Remove(friendship);

    public Task RemovePendingInvolvingAsync(Ulid userId, CancellationToken cancellationToken) =>
        _db.Friendships
            .Where(friendship => friendship.Status == FriendshipStatus.Pending
                && (friendship.RequesterId == userId || friendship.AddresseeId == userId))
            .ExecuteDeleteAsync(cancellationToken);
}

internal sealed class SocialBlockRepository : ISocialBlockRepository
{
    private readonly SocialDbContext _db;

    public SocialBlockRepository(SocialDbContext db) => _db = db;

    public Task<SocialBlock?> GetAsync(Ulid blockerId, Ulid blockedId, CancellationToken cancellationToken) =>
        _db.SocialBlocks.AsTracking().FirstOrDefaultAsync(
            block => block.BlockerId == blockerId && block.BlockedId == blockedId, cancellationToken);

    public Task<bool> AnyBetweenAsync(Ulid userA, Ulid userB, CancellationToken cancellationToken) =>
        _db.SocialBlocks.AnyAsync(
            block =>
                (block.BlockerId == userA && block.BlockedId == userB)
                || (block.BlockerId == userB && block.BlockedId == userA),
            cancellationToken);

    public void Add(SocialBlock block) => _db.SocialBlocks.Add(block);

    public void Remove(SocialBlock block) => _db.SocialBlocks.Remove(block);
}

internal sealed class GroupRepository : IGroupRepository
{
    private readonly SocialDbContext _db;

    public GroupRepository(SocialDbContext db) => _db = db;

    public Task<Group?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Groups.AsTracking().FirstOrDefaultAsync(group => group.Id == id, cancellationToken);

    public void Add(Group group) => _db.Groups.Add(group);

    public Task<GroupMembership?> GetMembershipAsync(Ulid groupId, Ulid userId, CancellationToken cancellationToken) =>
        _db.GroupMemberships.AsTracking().FirstOrDefaultAsync(
            membership => membership.GroupId == groupId && membership.UserId == userId, cancellationToken);

    public void AddMembership(GroupMembership membership) => _db.GroupMemberships.Add(membership);

    public void RemoveMembership(GroupMembership membership) => _db.GroupMemberships.Remove(membership);
}

internal sealed class GroupInviteRepository : IGroupInviteRepository
{
    private readonly SocialDbContext _db;

    public GroupInviteRepository(SocialDbContext db) => _db = db;

    public Task<GroupInvite?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.GroupInvites.AsTracking().FirstOrDefaultAsync(invite => invite.Id == id, cancellationToken);

    public Task<GroupInvite?> GetPendingAsync(Ulid groupId, Ulid invitedUserId, CancellationToken cancellationToken) =>
        _db.GroupInvites.AsTracking().FirstOrDefaultAsync(
            invite => invite.GroupId == groupId && invite.InvitedUserId == invitedUserId, cancellationToken);

    public void Add(GroupInvite invite) => _db.GroupInvites.Add(invite);

    public void Remove(GroupInvite invite) => _db.GroupInvites.Remove(invite);

    public Task RemoveBetweenAsync(Ulid userA, Ulid userB, CancellationToken cancellationToken) =>
        _db.GroupInvites
            .Where(invite =>
                (invite.InvitedUserId == userA && invite.InvitedBy == userB)
                || (invite.InvitedUserId == userB && invite.InvitedBy == userA))
            .ExecuteDeleteAsync(cancellationToken);

    public Task RemoveInvolvingAsync(Ulid userId, CancellationToken cancellationToken) =>
        _db.GroupInvites
            .Where(invite => invite.InvitedUserId == userId || invite.InvitedBy == userId)
            .ExecuteDeleteAsync(cancellationToken);
}

internal sealed class ConversationRepository : IConversationRepository
{
    private readonly SocialDbContext _db;

    public ConversationRepository(SocialDbContext db) => _db = db;

    public Task<Conversation?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Conversations.AsTracking().FirstOrDefaultAsync(conversation => conversation.Id == id, cancellationToken);

    public Task<Conversation?> GetDirectByKeyAsync(string directKey, CancellationToken cancellationToken) =>
        _db.Conversations.AsTracking().FirstOrDefaultAsync(
            conversation => conversation.DirectKey == directKey, cancellationToken);

    public void Add(Conversation conversation) => _db.Conversations.Add(conversation);

    public Task<ConversationParticipant?> GetParticipantAsync(
        Ulid conversationId, Ulid userId, CancellationToken cancellationToken) =>
        _db.ConversationParticipants.AsTracking().FirstOrDefaultAsync(
            participant => participant.ConversationId == conversationId && participant.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<ConversationParticipant>> GetActiveParticipantsAsync(
        Ulid conversationId, CancellationToken cancellationToken) =>
        await _db.ConversationParticipants.AsTracking()
            .Where(participant => participant.ConversationId == conversationId && participant.LeftOnUtc == null)
            .ToListAsync(cancellationToken);

    public void AddParticipant(ConversationParticipant participant) => _db.ConversationParticipants.Add(participant);

    public Task<Message?> GetMessageAsync(Ulid messageId, CancellationToken cancellationToken) =>
        // IgnoreQueryFilters: a tombstoned message must stay addressable (edit → 422, delete → 422, Files'
        // authorizer → 422) instead of morphing into a 404 that lies about existence.
        _db.Messages.AsTracking().IgnoreQueryFilters()
            .FirstOrDefaultAsync(message => message.Id == messageId, cancellationToken);

    public void AddMessage(Message message) => _db.Messages.Add(message);
}

internal sealed class NotificationRepository : INotificationRepository
{
    private readonly SocialDbContext _db;

    public NotificationRepository(SocialDbContext db) => _db = db;

    public void Add(Notification notification) => _db.Notifications.Add(notification);

    public Task<int> MarkReadAsync(Ulid userId, IReadOnlyList<Ulid>? ids, CancellationToken cancellationToken)
    {
        var unread = _db.Notifications.Where(
            notification => notification.UserId == userId && !notification.IsRead);
        if (ids is { Count: > 0 })
        {
            unread = unread.Where(notification => ids.Contains(notification.Id));
        }

        return unread.ExecuteUpdateAsync(
            static setters => setters.SetProperty(static notification => notification.IsRead, true),
            cancellationToken);
    }
}

internal sealed class PrivacySettingsRepository : IPrivacySettingsRepository
{
    private readonly SocialDbContext _db;

    public PrivacySettingsRepository(SocialDbContext db) => _db = db;

    public Task<UserPrivacySettings?> GetAsync(Ulid userId, CancellationToken cancellationToken) =>
        _db.PrivacySettings.AsTracking().FirstOrDefaultAsync(
            settings => settings.UserId == userId, cancellationToken);

    public void Add(UserPrivacySettings settings) => _db.PrivacySettings.Add(settings);
}

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly SocialDbContext _db;

    public UnitOfWork(SocialDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAndDispatchEventsAsync(cancellationToken);
}
