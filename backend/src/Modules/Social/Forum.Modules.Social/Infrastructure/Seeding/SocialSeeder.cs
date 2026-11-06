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
/// Seeds <c>forum_social</c>. Two shapes, one seeder — the ONLY module where the profile changes the graph and not
/// just the counts, because the Development cast is hand-shaped for the demo UI and must stay byte-for-byte stable:
/// <list type="bullet">
/// <item><description><b>Development</b> — the demo cast (admin/mod/alice/bob/charlie): friendships (2,3)(2,4)(3,4)(1,2)
/// accepted + (4,0) pending, book-club (public, alice) + staff-room (private, mod), bob promoted to book-club admin
/// THROUGH the real IAclGrantService (the group ACL path gets exercised, not hand-inserted), one pending staff-room
/// invite for bob, one alice↔bob DM, messages round-robined over the chats, bell rows for the pending request +
/// invite.</description></item>
/// <item><description><b>Benchmark</b> — Architecture B's ring rule, mirrored pair for pair through
/// <see cref="BenchSocialGraph"/> (A/B deviation D6): 2 400 accepted friendships (ring + chords 7 and 40), 80
/// pending requests with their <c>friend.request</c> bell rows, 800 direct conversations carrying 2 000 messages,
/// every fourth conversation marked read by its first user, no groups. Batched 1 000 rows per
/// <c>SaveChangesAsync</c> + <c>ChangeTracker.Clear()</c> like the other module seeders.</description></item>
/// </list>
/// Seed factories raise no events → zero outbox rows in both profiles.
/// </summary>
internal sealed class SocialSeeder : IModuleSeeder
{
    private const int BatchSize = 1000;

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

    // Rows added since the last flush — the Benchmark path interleaves entity types, so a per-type buffer
    // (ContentSeeder's idiom) would not bound the tracker; a plain counter does.
    private int _tracked;

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
            return; // Nothing social in this profile.
        }

        switch (plan.Profile)
        {
            case SeedProfile.Development:
                await SeedDemoCastAsync(plan, cancellationToken);
                break;
            case SeedProfile.Benchmark:
                await SeedBenchmarkRingAsync(plan, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"No social seed shape is defined for profile {plan.Profile}.");
        }
    }

    private static Ulid UserId(int index) => SeedUlids.Create(SeedStreams.User, index);

    // ---- Development: the hand-shaped demo cast (unchanged since Phase 11 — its output is pinned by SeedFlowTests).
    private async Task SeedDemoCastAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
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

    // ---- Benchmark: B's ring rule (gomx seedbench.go benchFriendships + benchDirectMessages), stage by stage in
    // B's own order. Every id/timestamp is SeedUlids/SeedTime of (stream, index); the indices come from the graph.
    private async Task SeedBenchmarkRingAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        if (plan.UserCount < 3)
        {
            // Below three users ring positions p and p+1 stop naming distinct pairs, and the conversation-per-position
            // keying below would collide on direct_key.
            throw new InvalidOperationException("The Benchmark social ring needs at least three users.");
        }

        if (plan.GroupCount != 0 || plan.GroupInviteCount != 0)
        {
            throw new InvalidOperationException("The Benchmark social profile seeds no groups (Architecture B parity).");
        }

        var accepted = BenchSocialGraph.AcceptedPairs(plan.UserCount);
        var pending = BenchSocialGraph.PendingPairs(plan.UserCount, plan.PendingFriendshipCount);
        var conversationCount = Math.Min(plan.MessageCount, plan.UserCount); // ring positions a message reaches
        if (accepted.Count != plan.FriendshipCount
            || pending.Count != plan.PendingFriendshipCount
            || conversationCount != plan.DirectConversationCount)
        {
            throw new InvalidOperationException(
                $"SeedPlan social counts ({plan.FriendshipCount}/{plan.PendingFriendshipCount}/{plan.DirectConversationCount}) " +
                $"disagree with the ring rule ({accepted.Count}/{pending.Count}/{conversationCount}).");
        }

        // 1. Accepted friendships — B's loop order (chord-major, user-minor) IS the friendship stream order.
        for (var i = 0; i < accepted.Count; i++)
        {
            var (requester, addressee) = accepted[i];
            _db.Friendships.Add(Friendship.Seed(
                SeedUlids.Create(SeedStreams.Friendship, i),
                UserId(requester), UserId(addressee), SeedTime.At(SeedStreams.Friendship, i), accepted: true));
            await TrackAsync(1, cancellationToken);
        }

        // 2. Pending requests continue the same stream; each gets its friend.request bell row for the addressee
        //    (what SendFriendRequest does at runtime, and what the Development cast seeds too).
        for (var k = 0; k < pending.Count; k++)
        {
            var index = plan.FriendshipCount + k;
            var (requester, addressee) = pending[k];
            var friendshipId = SeedUlids.Create(SeedStreams.Friendship, index);
            _db.Friendships.Add(Friendship.Seed(
                friendshipId, UserId(requester), UserId(addressee), SeedTime.At(SeedStreams.Friendship, index), accepted: false));
            _db.Notifications.Add(new Notification(
                SeedUlids.Create(SeedStreams.SocialNotification, k),
                UserId(addressee), NotificationKinds.FriendRequest, UserId(requester), friendshipId,
                SeedTime.At(SeedStreams.SocialNotification, k)));
            await TrackAsync(2, cancellationToken);
        }

        await FlushAsync(cancellationToken);

        // 3. Direct conversations — one per ring position p a message reaches (B get-or-creates it on the first
        //    DM), keyed by p in the conversation stream, seats users[p] + users[(p+1) mod n]. B's
        //    MarkDMConversationRead(users[p], users[p+1]) on every fourth pair becomes the users[p] seat's
        //    last_read_on_utc = the pair's latest message (B's cursor = latest message id, either direction).
        var readMarkers = BenchSocialGraph.ReadMarkerPairs(plan.UserCount).ToHashSet();
        var readMarkerCount = 0;
        for (var pair = 0; pair < conversationCount; pair++)
        {
            var first = pair;
            var second = (pair + 1) % plan.UserCount;
            var conversationId = SeedUlids.Create(SeedStreams.Conversation, pair);
            var createdOn = SeedTime.At(SeedStreams.Conversation, pair);

            _db.Conversations.Add(Conversation.Seed(
                conversationId, ConversationType.Direct, Conversation.BuildDirectKey(UserId(first), UserId(second)), createdOn));

            var firstSeat = new ConversationParticipant(conversationId, UserId(first), createdOn);
            if (readMarkers.Contains(pair)
                && BenchSocialGraph.LastMessageIndex(pair, plan.UserCount, plan.MessageCount) is { } lastMessage)
            {
                firstSeat.MarkRead(SeedTime.At(SeedStreams.Message, lastMessage));
                readMarkerCount++;
            }

            _db.ConversationParticipants.Add(firstSeat);
            _db.ConversationParticipants.Add(new ConversationParticipant(conversationId, UserId(second), createdOn));
            await TrackAsync(3, cancellationToken);
        }

        await FlushAsync(cancellationToken); // Conversations are committed before their messages (FK).

        // 4. Messages — message i on ring pair i mod n, users[p] sending on even i, the other side on odd i.
        //    Bodies are not part of the parity contract; B-shaped "Benchmark DM n — …" over the message bank.
        for (var m = 0; m < plan.MessageCount; m++)
        {
            var message = BenchSocialGraph.MessageAt(m, plan.UserCount);
            _db.Messages.Add(Message.Seed(
                SeedUlids.Create(SeedStreams.Message, m),
                SeedUlids.Create(SeedStreams.Conversation, message.Pair),
                UserId(message.Sender),
                $"Benchmark DM {m + 1} — {MessageBank[m % MessageBank.Length]}",
                SeedTime.At(SeedStreams.Message, m)));
            await TrackAsync(1, cancellationToken);
        }

        await FlushAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Social seeded (B ring rule): {Friendships}+{Pending} friendships, {Conversations} direct conversations, {Messages} messages, {ReadMarkers} read markers, 0 groups.",
                accepted.Count, pending.Count, conversationCount, plan.MessageCount, readMarkerCount);
        }
    }

    private async Task TrackAsync(int added, CancellationToken cancellationToken)
    {
        _tracked += added;
        if (_tracked >= BatchSize)
        {
            await FlushAsync(cancellationToken);
        }
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_tracked == 0)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        _tracked = 0;
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
