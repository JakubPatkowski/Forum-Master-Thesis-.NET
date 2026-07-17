using System.Text.Json;

using Forum.Api.Realtime;
using Forum.Infrastructure.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Contracts.IntegrationEvents;
using Forum.Modules.Social.Contracts.IntegrationEvents;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>
/// The hub's event catalog: every consumed integration event maps to the ADR 0010 envelope with the right
/// type/entity, the right visibility scope and the right routes (ADR 0011), and anything unknown or malformed is
/// dropped (never thrown).
/// </summary>
public sealed class RealtimeEventMapTests
{
    private readonly Ulid _categoryId = Ulid.NewUlid();
    private readonly Ulid _threadId = Ulid.NewUlid();
    private readonly Ulid _commentId = Ulid.NewUlid();
    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly Ulid _otherUserId = Ulid.NewUlid();
    private readonly Ulid _groupId = Ulid.NewUlid();
    private readonly Ulid _conversationId = Ulid.NewUlid();

    private static RealtimeNotification Map<TEvent>(TEvent integrationEvent)
        where TEvent : class
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, IntegrationEventJson.SerializerOptions);
        RealtimeEventMap.TryMap(typeof(TEvent).Name, body, out var notification).ShouldBeTrue();
        return notification!;
    }

    [Fact]
    public void Thread_created_maps_to_a_created_thread_notification_scoped_to_its_category()
    {
        var notification = Map(new ThreadCreatedIntegrationEvent(
            Ulid.NewUlid(), _threadId, _categoryId, _userId, "Title", DateTimeOffset.UtcNow));

        notification.Payload.ShouldBe(new ChangeNotification(
            "created", "thread", _threadId.ToString(), null, _categoryId.ToString()));
        notification.Visibility.ShouldBe(RealtimeVisibility.Category(_categoryId));
        notification.Routes.ShouldBe([
            new SubscriptionView(ViewKind.Category, _categoryId),
            new SubscriptionView(ViewKind.Thread, _threadId),
        ]);
    }

    [Fact]
    public void Thread_updated_and_deleted_map_to_their_notification_types()
    {
        Map(new ThreadUpdatedIntegrationEvent(Ulid.NewUlid(), _threadId, _categoryId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("updated");
        Map(new ThreadDeletedIntegrationEvent(Ulid.NewUlid(), _threadId, _categoryId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("deleted");
    }

    [Fact]
    public void Comment_events_carry_the_thread_as_parent()
    {
        var created = Map(new CommentCreatedIntegrationEvent(
            Ulid.NewUlid(), _commentId, _threadId, ParentId: null, _userId, _categoryId, DateTimeOffset.UtcNow));
        created.Payload.ShouldBe(new ChangeNotification(
            "created", "comment", _commentId.ToString(), _threadId.ToString(), _categoryId.ToString()));
        created.Routes.ShouldContain(new SubscriptionView(ViewKind.Thread, _threadId));

        Map(new CommentUpdatedIntegrationEvent(Ulid.NewUlid(), _commentId, _threadId, _categoryId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("updated");
        Map(new CommentDeletedIntegrationEvent(Ulid.NewUlid(), _commentId, _threadId, _categoryId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("deleted");
    }

    [Fact]
    public void Reaction_events_identify_the_target_and_route_to_the_acting_users_own_view()
    {
        var added = Map(new ReactionAddedIntegrationEvent(
            Ulid.NewUlid(), _userId, "comment", _commentId, "like", _categoryId, _threadId, DateTimeOffset.UtcNow));

        added.Payload.ShouldBe(new ChangeNotification(
            "created", "reaction", _commentId.ToString(), _threadId.ToString(), _categoryId.ToString()));
        added.Routes.ShouldContain(new SubscriptionView(ViewKind.User, _userId));

        var removed = Map(new ReactionRemovedIntegrationEvent(
            Ulid.NewUlid(), _userId, "thread", _threadId, "like", _categoryId, _threadId, DateTimeOffset.UtcNow));
        removed.Payload.Type.ShouldBe("deleted");
        removed.Payload.Id.ShouldBe(_threadId.ToString());
    }

    [Fact]
    public void Friendship_events_route_to_both_users_views_with_no_per_push_check()
    {
        var friendshipId = Ulid.NewUlid();
        var sent = Map(new FriendRequestSentIntegrationEvent(
            Ulid.NewUlid(), friendshipId, _userId, _otherUserId, DateTimeOffset.UtcNow));

        sent.Payload.ShouldBe(new ChangeNotification(
            "created", "friendship", friendshipId.ToString(), null, null));
        sent.Visibility.ShouldBe(RealtimeVisibility.TargetUsers);
        sent.Routes.ShouldBe([
            new SubscriptionView(ViewKind.User, _userId),
            new SubscriptionView(ViewKind.User, _otherUserId),
        ]);

        Map(new FriendRequestAcceptedIntegrationEvent(
            Ulid.NewUlid(), friendshipId, _userId, _otherUserId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("updated");
        Map(new FriendRemovedIntegrationEvent(
            Ulid.NewUlid(), friendshipId, _userId, _otherUserId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("deleted");
    }

    [Fact]
    public void Group_events_are_participant_gated_and_route_to_the_group_view()
    {
        var updated = Map(new GroupUpdatedIntegrationEvent(Ulid.NewUlid(), _groupId, DateTimeOffset.UtcNow));

        updated.Payload.ShouldBe(new ChangeNotification(
            "updated", "group", _groupId.ToString(), _groupId.ToString(), null));
        updated.Visibility.ShouldBe(RealtimeVisibility.Conversation(_groupId));
        updated.Routes.ShouldBe([new SubscriptionView(ViewKind.Group, _groupId)]);

        var left = Map(new GroupMemberLeftIntegrationEvent(
            Ulid.NewUlid(), _groupId, _userId, Removed: true, _otherUserId, DateTimeOffset.UtcNow));
        left.Payload.Entity.ShouldBe("group_member");
        left.Payload.Type.ShouldBe("deleted");
    }

    [Fact]
    public void Direct_messages_route_to_the_conversation_and_both_participants_user_views()
    {
        var messageId = Ulid.NewUlid();
        var sent = Map(new MessageSentIntegrationEvent(
            Ulid.NewUlid(), messageId, _conversationId, "direct", _userId, [_userId, _otherUserId],
            DateTimeOffset.UtcNow));

        sent.Payload.ShouldBe(new ChangeNotification(
            "created", "message", messageId.ToString(), _conversationId.ToString(), null));
        sent.Visibility.ShouldBe(RealtimeVisibility.Conversation(_conversationId));
        sent.Routes.ShouldBe([
            new SubscriptionView(ViewKind.Conversation, _conversationId),
            new SubscriptionView(ViewKind.User, _userId),
            new SubscriptionView(ViewKind.User, _otherUserId),
        ]);
    }

    [Fact]
    public void Group_messages_route_to_the_conversation_and_group_views_never_user_views()
    {
        var sent = Map(new MessageSentIntegrationEvent(
            Ulid.NewUlid(), Ulid.NewUlid(), _groupId, "group", _userId, [], DateTimeOffset.UtcNow));

        sent.Visibility.ShouldBe(RealtimeVisibility.Conversation(_groupId));
        sent.Routes.ShouldBe([
            new SubscriptionView(ViewKind.Conversation, _groupId),
            new SubscriptionView(ViewKind.Group, _groupId),
        ]);
    }

    [Fact]
    public void Notification_created_pings_only_the_recipients_user_view()
    {
        var notificationId = Ulid.NewUlid();
        var created = Map(new NotificationCreatedIntegrationEvent(
            Ulid.NewUlid(), notificationId, _userId, DateTimeOffset.UtcNow));

        created.Payload.ShouldBe(new ChangeNotification(
            "created", "notification", notificationId.ToString(), null, null));
        created.Visibility.ShouldBe(RealtimeVisibility.TargetUsers);
        created.Routes.ShouldBe([new SubscriptionView(ViewKind.User, _userId)]);
    }

    [Fact]
    public void Unknown_routing_keys_and_malformed_payloads_are_dropped_not_thrown()
    {
        RealtimeEventMap.TryMap("UserRegisteredIntegrationEvent", "{}"u8, out _).ShouldBeFalse();
        RealtimeEventMap.TryMap("ThreadCreatedIntegrationEvent", "this is not json"u8, out _).ShouldBeFalse();
        RealtimeEventMap.TryMap("GroupCreatedIntegrationEvent", "{}"u8, out _).ShouldBeFalse();
    }
}
