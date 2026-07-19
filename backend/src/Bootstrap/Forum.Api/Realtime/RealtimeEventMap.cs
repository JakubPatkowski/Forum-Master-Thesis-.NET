using System.Text.Json;

using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.RabbitMq;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Contracts.IntegrationEvents;
using Forum.Modules.Social.Contracts.IntegrationEvents;

namespace Forum.Api.Realtime;

/// <summary>
/// The hub's event catalog: which integration events fan out to sockets and how each becomes a
/// <see cref="RealtimeNotification"/> — payload envelope + visibility scope + the routes it matches (ADR 0011).
/// Content/Engagement events authorize at category scope; Social's chat/group events at conversation scope
/// (participant check; a group chat's conversation id IS the group id); friendship/invite/bell events route to
/// user views only, which are subscribe-time self-gated, so they need no per-push check. Consciously not wired:
/// Identity events, <c>CategoryCreated</c>, <c>FileCommitted</c>, <c>GroupCreated</c> (nobody but the creator can
/// watch a group that just came into being) and presence (never on the bus by design).
/// </summary>
internal static class RealtimeEventMap
{
    public static IReadOnlyList<Type> ConsumedEvents { get; } =
    [
        typeof(ThreadCreatedIntegrationEvent),
        typeof(ThreadUpdatedIntegrationEvent),
        typeof(ThreadDeletedIntegrationEvent),
        typeof(CommentCreatedIntegrationEvent),
        typeof(CommentUpdatedIntegrationEvent),
        typeof(CommentDeletedIntegrationEvent),
        typeof(ReactionAddedIntegrationEvent),
        typeof(ReactionRemovedIntegrationEvent),
        typeof(FriendRequestSentIntegrationEvent),
        typeof(FriendRequestAcceptedIntegrationEvent),
        typeof(FriendRequestDeclinedIntegrationEvent),
        typeof(FriendRemovedIntegrationEvent),
        typeof(GroupUpdatedIntegrationEvent),
        typeof(GroupDeletedIntegrationEvent),
        typeof(GroupInviteSentIntegrationEvent),
        typeof(GroupInviteRespondedIntegrationEvent),
        typeof(GroupMemberJoinedIntegrationEvent),
        typeof(GroupMemberLeftIntegrationEvent),
        typeof(MessageSentIntegrationEvent),
        typeof(MessageEditedIntegrationEvent),
        typeof(MessageDeletedIntegrationEvent),
        typeof(NotificationCreatedIntegrationEvent),
    ];

    private static readonly Dictionary<string, Type> ByRoutingKey =
        ConsumedEvents.ToDictionary(MessagingTopology.RoutingKey);

    /// <summary>
    /// Maps a raw delivery to a routed notification. False for unknown routing keys and undeserializable
    /// payloads — the hub drops those silently (a missed push self-heals through the client's resync; the
    /// durable module consumers, not the hub, own poison handling).
    /// </summary>
    public static bool TryMap(string routingKey, ReadOnlySpan<byte> body, out RealtimeNotification? notification)
    {
        notification = null;
        if (!ByRoutingKey.TryGetValue(routingKey, out var eventType))
        {
            return false;
        }

        object? integrationEvent;
        try
        {
            integrationEvent = JsonSerializer.Deserialize(body, eventType, IntegrationEventJson.SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException)
        {
            return false;
        }

        notification = integrationEvent switch
        {
            ThreadCreatedIntegrationEvent e => Content(
                ChangeNotification.Created, ChangeNotification.ThreadEntity, e.ThreadId, null, e.CategoryId, e.ThreadId),
            ThreadUpdatedIntegrationEvent e => Content(
                ChangeNotification.Updated, ChangeNotification.ThreadEntity, e.ThreadId, null, e.CategoryId, e.ThreadId),
            ThreadDeletedIntegrationEvent e => Content(
                ChangeNotification.Deleted, ChangeNotification.ThreadEntity, e.ThreadId, null, e.CategoryId, e.ThreadId),
            CommentCreatedIntegrationEvent e => Content(
                ChangeNotification.Created, ChangeNotification.CommentEntity, e.CommentId, e.ThreadId, e.CategoryId, e.ThreadId),
            CommentUpdatedIntegrationEvent e => Content(
                ChangeNotification.Updated, ChangeNotification.CommentEntity, e.CommentId, e.ThreadId, e.CategoryId, e.ThreadId),
            CommentDeletedIntegrationEvent e => Content(
                ChangeNotification.Deleted, ChangeNotification.CommentEntity, e.CommentId, e.ThreadId, e.CategoryId, e.ThreadId),
            ReactionAddedIntegrationEvent e => Content(
                ChangeNotification.Created, ChangeNotification.ReactionEntity, e.TargetId, e.ThreadId, e.CategoryId, e.ThreadId, e.UserId),
            ReactionRemovedIntegrationEvent e => Content(
                ChangeNotification.Deleted, ChangeNotification.ReactionEntity, e.TargetId, e.ThreadId, e.CategoryId, e.ThreadId, e.UserId),

            FriendRequestSentIntegrationEvent e => Users(
                ChangeNotification.Created, ChangeNotification.FriendshipEntity, e.FriendshipId, e.RequesterId, e.AddresseeId),
            FriendRequestAcceptedIntegrationEvent e => Users(
                ChangeNotification.Updated, ChangeNotification.FriendshipEntity, e.FriendshipId, e.RequesterId, e.AddresseeId),
            FriendRequestDeclinedIntegrationEvent e => Users(
                ChangeNotification.Deleted, ChangeNotification.FriendshipEntity, e.FriendshipId, e.RequesterId, e.AddresseeId),
            FriendRemovedIntegrationEvent e => Users(
                ChangeNotification.Deleted, ChangeNotification.FriendshipEntity, e.FriendshipId, e.RequesterId, e.AddresseeId),

            GroupUpdatedIntegrationEvent e => Group(
                ChangeNotification.Updated, ChangeNotification.GroupEntity, e.GroupId, e.GroupId),
            GroupDeletedIntegrationEvent e => Group(
                ChangeNotification.Deleted, ChangeNotification.GroupEntity, e.GroupId, e.GroupId),
            GroupMemberJoinedIntegrationEvent e => Group(
                ChangeNotification.Created, ChangeNotification.GroupMemberEntity, e.UserId, e.GroupId),
            GroupMemberLeftIntegrationEvent e => Group(
                ChangeNotification.Deleted, ChangeNotification.GroupMemberEntity, e.UserId, e.GroupId),

            GroupInviteSentIntegrationEvent e => Users(
                ChangeNotification.Created, ChangeNotification.GroupInviteEntity, e.InviteId, e.InvitedUserId, e.InvitedBy, e.GroupId),
            GroupInviteRespondedIntegrationEvent e => Users(
                e.Accepted ? ChangeNotification.Updated : ChangeNotification.Deleted,
                ChangeNotification.GroupInviteEntity, e.InviteId, e.InvitedUserId, e.InvitedBy, e.GroupId),

            MessageSentIntegrationEvent e => Message(
                ChangeNotification.Created, e.MessageId, e.ConversationId, e.ConversationType, e.DirectParticipantIds),
            MessageEditedIntegrationEvent e => Message(
                ChangeNotification.Updated, e.MessageId, e.ConversationId, e.ConversationType, e.DirectParticipantIds),
            MessageDeletedIntegrationEvent e => Message(
                ChangeNotification.Deleted, e.MessageId, e.ConversationId, e.ConversationType, e.DirectParticipantIds),

            NotificationCreatedIntegrationEvent e => Users(
                ChangeNotification.Created, ChangeNotification.NotificationEntity, e.NotificationId, e.UserId),

            _ => null,
        };
        return notification is not null;
    }

    /// <summary>Content/Engagement: category-scoped visibility; routes = category + thread (+ actor's own devices).</summary>
    private static RealtimeNotification Content(
        string type, string entity, Ulid id, Ulid? parentId, Ulid categoryId, Ulid threadId, Ulid? actorUserId = null)
    {
        List<SubscriptionView> routes =
        [
            new(ViewKind.Category, categoryId),
            new(ViewKind.Thread, threadId),
        ];
        if (actorUserId is { } actor)
        {
            routes.Add(new SubscriptionView(ViewKind.User, actor));
        }

        return new RealtimeNotification(
            new ChangeNotification(type, entity, id.ToString(), parentId?.ToString(), categoryId.ToString()),
            RealtimeVisibility.Category(categoryId),
            routes);
    }

    /// <summary>Group-scoped events: participant-checked, routed to the group view (conversation id == group id).</summary>
    private static RealtimeNotification Group(string type, string entity, Ulid id, Ulid groupId) =>
        new(
            new ChangeNotification(type, entity, id.ToString(), groupId.ToString(), CategoryId: null),
            RealtimeVisibility.Conversation(groupId),
            [new SubscriptionView(ViewKind.Group, groupId)]);

    /// <summary>Chat messages: participant-checked; DMs also hit both users' user views (badge path), group
    /// chats also hit the group view (no per-member fan-out — the documented tradeoff).</summary>
    private static RealtimeNotification Message(
        string type, Ulid messageId, Ulid conversationId, string conversationType, IReadOnlyList<Ulid> directParticipants)
    {
        List<SubscriptionView> routes = [new(ViewKind.Conversation, conversationId)];
        if (string.Equals(conversationType, "group", StringComparison.OrdinalIgnoreCase))
        {
            routes.Add(new SubscriptionView(ViewKind.Group, conversationId));
        }
        else
        {
            foreach (var participant in directParticipants)
            {
                routes.Add(new SubscriptionView(ViewKind.User, participant));
            }
        }

        return new RealtimeNotification(
            new ChangeNotification(
                type, ChangeNotification.MessageEntity, messageId.ToString(), conversationId.ToString(), CategoryId: null),
            RealtimeVisibility.Conversation(conversationId),
            routes);
    }

    /// <summary>User-view-only events (friendships, invites, the bell): self-gated at subscribe, no per-push check.</summary>
    private static RealtimeNotification Users(
        string type, string entity, Ulid id, Ulid firstUser, Ulid? secondUser = null, Ulid? parentId = null)
    {
        List<SubscriptionView> routes = [new(ViewKind.User, firstUser)];
        if (secondUser is { } second && second != firstUser)
        {
            routes.Add(new SubscriptionView(ViewKind.User, second));
        }

        return new RealtimeNotification(
            new ChangeNotification(type, entity, id.ToString(), parentId?.ToString(), CategoryId: null),
            RealtimeVisibility.TargetUsers,
            routes);
    }
}
