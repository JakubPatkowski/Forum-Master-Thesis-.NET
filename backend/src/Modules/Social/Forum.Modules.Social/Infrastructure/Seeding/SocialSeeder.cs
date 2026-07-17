using Forum.Common.Security;
using Forum.Infrastructure.Seeding;
using Forum.Modules.Social.Domain.Conversations;
using Forum.Modules.Social.Domain.Friendships;
using Forum.Modules.Social.Domain.Groups;
using Forum.Modules.Social.Domain.Notifications;
using Forum.Modules.Social.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Forum.Modules.Social.Infrastructure.Seeding;

/// <summary>
/// Seeds <c>forum_social</c> for the Development demo cast (admin/mod/alice/bob/charlie). Benchmark counts are
/// deliberately ZERO — the A/B parity numbers are blocked on Hubert (POST-9C-ROADMAP Decision 3), so this seeder
/// simply no-ops for that profile rather than inventing them. The pair/member layout is a fixed deterministic
/// graph over user indices (the PrivateCategoryMemberIndices idiom, hand-sized for five users): friendships
/// (2,3)(2,4)(3,4)(1,2) accepted + (4,0) pending, book-club (public, alice) + staff-room (private, mod), bob
/// promoted to book-club admin THROUGH the real IAclGrantService (the group ACL path gets exercised, not
/// hand-inserted), one pending staff-room invite for bob, one alice↔bob DM, messages round-robined over the
/// chats, bell rows for the pending request + invite. Seed factories raise no events → zero outbox rows.
/// </summary>
internal sealed class SocialSeeder : IModuleSeeder
{
    private static readonly (int Requester, int Addressee)[] AcceptedPairs = [(2, 3), (2, 4), (3, 4), (1, 2)];
    private static readonly (int Requester, int Addressee)[] PendingPairs = [(4, 0)];
    private static readonly string[] GroupNames = ["book-club", "staff-room"];
    private static readonly int[][] GroupMemberIndices = [[2, 3, 4], [1, 0, 2]];
    private static readonly string[] MessageBank =
    [
        "Hey, did you catch the new thread in general?",
        "Yes! Replying after lunch.",
        "Anyone up for reviewing my draft?",
        "Sure, send it over.",
        "This group is exactly what we needed.",
        "Agreed — welcome aboard, everyone.",
        "Reminder: meetup notes go here.",
        "Uploaded the photos from Saturday.",
        "Nice shots! The second one is great.",
        "Let's pin the reading list next week.",
        "Done — see the updated description.",
        "Thanks all, see you around!",
    ];

    private readonly SocialDbContext _db;
    private readonly IAclGrantService _aclGrants;
    private readonly ILogger<SocialSeeder> _logger;

    public SocialSeeder(SocialDbContext db, IAclGrantService aclGrants, ILogger<SocialSeeder> logger)
    {
        _db = db;
        _aclGrants = aclGrants;
        _logger = logger;
    }

    public int Order => 4;

    public async Task SeedAsync(SeedConfig config, CancellationToken cancellationToken)
    {
        var plan = SeedPlan.For(config.Profile);

        if (config.AllowTruncate)
        {
            await TruncateAsync(cancellationToken);
        }

        if (plan.FriendshipCount == 0 && plan.GroupCount == 0)
        {
            return; // Benchmark: blocked on the A/B parity agreement — deliberately empty.
        }

        static Ulid UserId(int index) => SeedUlids.Create(SeedStreams.User, index);

        // Friendships: accepted pairs first, pending after them (same stream, later indices).
        for (var i = 0; i < plan.FriendshipCount && i < AcceptedPairs.Length; i++)
        {
            var (requester, addressee) = AcceptedPairs[i];
            _db.Friendships.Add(Friendship.Seed(
                SeedUlids.Create(SeedStreams.Friendship, i),
                UserId(requester), UserId(addressee), SeedTime.At(SeedStreams.Friendship, i), accepted: true));
        }

        var pendingFriendshipIds = new List<Ulid>();
        for (var i = 0; i < plan.PendingFriendshipCount && i < PendingPairs.Length; i++)
        {
            var index = plan.FriendshipCount + i;
            var (requester, addressee) = PendingPairs[i];
            var id = SeedUlids.Create(SeedStreams.Friendship, index);
            pendingFriendshipIds.Add(id);
            _db.Friendships.Add(Friendship.Seed(
                id, UserId(requester), UserId(addressee), SeedTime.At(SeedStreams.Friendship, index), accepted: false));
        }

        // Groups + memberships + their chat conversations (conversation id == group id) + seats.
        var conversationIds = new List<Ulid>();
        var conversationMembers = new List<int[]>();
        for (var g = 0; g < plan.GroupCount; g++)
        {
            var groupId = SeedUlids.Create(SeedStreams.Group, g);
            var createdOn = SeedTime.At(SeedStreams.Group, g);
            var members = GroupMemberIndices[g % GroupMemberIndices.Length];
            var owner = members[0];
            var isPrivate = g >= plan.GroupCount - plan.PrivateGroupCount;

            _db.Groups.Add(Group.Seed(
                groupId,
                g < GroupNames.Length ? GroupNames[g] : $"group-{g}",
                $"Seeded demo group #{g}.",
                isPrivate ? GroupVisibility.Private : GroupVisibility.Public,
                UserId(owner),
                createdOn));
            _db.Conversations.Add(Conversation.Seed(groupId, ConversationType.Group, directKey: null, createdOn));

            foreach (var member in members)
            {
                _db.GroupMemberships.Add(new GroupMembership(
                    groupId, UserId(member), createdOn, member == owner ? null : UserId(owner)));
                _db.ConversationParticipants.Add(new ConversationParticipant(groupId, UserId(member), createdOn));
            }

            conversationIds.Add(groupId);
            conversationMembers.Add(members);
        }

        // Direct conversations (alice↔bob first) + seats.
        for (var d = 0; d < plan.DirectConversationCount; d++)
        {
            var (a, b) = AcceptedPairs[d % AcceptedPairs.Length];
            var conversationId = SeedUlids.Create(SeedStreams.Conversation, d);
            var createdOn = SeedTime.At(SeedStreams.Conversation, d);
            _db.Conversations.Add(Conversation.Seed(
                conversationId, ConversationType.Direct, Conversation.BuildDirectKey(UserId(a), UserId(b)), createdOn));
            _db.ConversationParticipants.Add(new ConversationParticipant(conversationId, UserId(a), createdOn));
            _db.ConversationParticipants.Add(new ConversationParticipant(conversationId, UserId(b), createdOn));
            conversationIds.Insert(d, conversationId);
            conversationMembers.Insert(d, [a, b]);
        }

        // Messages: round-robin across every conversation, senders cycling that chat's members.
        for (var m = 0; m < plan.MessageCount && conversationIds.Count > 0; m++)
        {
            var conversation = m % conversationIds.Count;
            var senderIndex = conversationMembers[conversation][(m / conversationIds.Count) % conversationMembers[conversation].Length];
            _db.Messages.Add(Message.Seed(
                SeedUlids.Create(SeedStreams.Message, m),
                conversationIds[conversation],
                UserId(senderIndex),
                MessageBank[m % MessageBank.Length],
                SeedTime.At(SeedStreams.Message, m)));
        }

        // Pending group invites (staff-room → bob) + the unread bell rows for everything actionable.
        var notificationIndex = 0;
        for (var i = 0; i < plan.GroupInviteCount && plan.GroupCount > 0; i++)
        {
            var groupIndex = plan.GroupCount - 1; // The private group is the inviting one in the demo cast.
            var inviteId = SeedUlids.Create(SeedStreams.GroupInvite, i);
            var inviter = GroupMemberIndices[groupIndex % GroupMemberIndices.Length][0];
            _db.GroupInvites.Add(GroupInvite.Seed(
                inviteId, SeedUlids.Create(SeedStreams.Group, groupIndex), UserId(3), UserId(inviter),
                SeedTime.At(SeedStreams.GroupInvite, i)));
            _db.Notifications.Add(new Notification(
                SeedUlids.Create(SeedStreams.SocialNotification, notificationIndex),
                UserId(3), NotificationKinds.GroupInvite, UserId(inviter), inviteId,
                SeedTime.At(SeedStreams.SocialNotification, notificationIndex)));
            notificationIndex++;
        }

        for (var i = 0; i < pendingFriendshipIds.Count; i++)
        {
            var (requester, addressee) = PendingPairs[i];
            _db.Notifications.Add(new Notification(
                SeedUlids.Create(SeedStreams.SocialNotification, notificationIndex),
                UserId(addressee), NotificationKinds.FriendRequest, UserId(requester), pendingFriendshipIds[i],
                SeedTime.At(SeedStreams.SocialNotification, notificationIndex)));
            notificationIndex++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();

        // Bob becomes book-club admin through the REAL grant path (ACL row + synchronous cache recompute) —
        // the seeded database exercises moderate@group exactly the way runtime promotion does.
        if (plan.GroupCount > 0)
        {
            await _aclGrants.GrantAsync(
                UserId(3), Permissions.Moderate, PermissionScopes.Group,
                SeedUlids.Create(SeedStreams.Group, 0), cancellationToken);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Social seeded: {Friendships}+{Pending} friendships, {Groups} groups, {Conversations} conversations, {Messages} messages.",
                plan.FriendshipCount, plan.PendingFriendshipCount, plan.GroupCount,
                conversationIds.Count, plan.MessageCount);
        }
    }

    private Task<int> TruncateAsync(CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE forum_social.friendships, forum_social.social_blocks, forum_social.groups,
                     forum_social.group_memberships, forum_social.group_invites, forum_social.conversations,
                     forum_social.conversation_participants, forum_social.messages, forum_social.notifications,
                     forum_social.user_privacy_settings, forum_social.user_presence,
                     forum_social.outbox_messages, forum_social.inbox_messages
            RESTART IDENTITY CASCADE
            """,
            cancellationToken);
}
