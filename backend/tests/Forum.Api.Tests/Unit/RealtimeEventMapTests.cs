using System.Text.Json;

using Forum.Api.Realtime;
using Forum.Infrastructure.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Contracts.IntegrationEvents;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>
/// The hub's event catalog: every consumed integration event maps to the ADR 0010 envelope with the right
/// type/entity/routing, and anything unknown or malformed is dropped (never thrown).
/// </summary>
public sealed class RealtimeEventMapTests
{
    private readonly Ulid _categoryId = Ulid.NewUlid();
    private readonly Ulid _threadId = Ulid.NewUlid();
    private readonly Ulid _commentId = Ulid.NewUlid();
    private readonly Ulid _userId = Ulid.NewUlid();

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
        notification.CategoryId.ShouldBe(_categoryId);
        notification.ThreadId.ShouldBe(_threadId);
        notification.ActorUserId.ShouldBeNull();
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
        created.ThreadId.ShouldBe(_threadId);

        Map(new CommentUpdatedIntegrationEvent(Ulid.NewUlid(), _commentId, _threadId, _categoryId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("updated");
        Map(new CommentDeletedIntegrationEvent(Ulid.NewUlid(), _commentId, _threadId, _categoryId, DateTimeOffset.UtcNow))
            .Payload.Type.ShouldBe("deleted");
    }

    [Fact]
    public void Reaction_events_identify_the_target_and_the_acting_user()
    {
        var added = Map(new ReactionAddedIntegrationEvent(
            Ulid.NewUlid(), _userId, "comment", _commentId, "like", _categoryId, _threadId, DateTimeOffset.UtcNow));

        added.Payload.ShouldBe(new ChangeNotification(
            "created", "reaction", _commentId.ToString(), _threadId.ToString(), _categoryId.ToString()));
        added.ActorUserId.ShouldBe(_userId);

        var removed = Map(new ReactionRemovedIntegrationEvent(
            Ulid.NewUlid(), _userId, "thread", _threadId, "like", _categoryId, _threadId, DateTimeOffset.UtcNow));
        removed.Payload.Type.ShouldBe("deleted");
        removed.Payload.Id.ShouldBe(_threadId.ToString());
    }

    [Fact]
    public void Unknown_routing_keys_and_malformed_payloads_are_dropped_not_thrown()
    {
        RealtimeEventMap.TryMap("UserRegisteredIntegrationEvent", "{}"u8, out _).ShouldBeFalse();
        RealtimeEventMap.TryMap("ThreadCreatedIntegrationEvent", "this is not json"u8, out _).ShouldBeFalse();
    }
}
