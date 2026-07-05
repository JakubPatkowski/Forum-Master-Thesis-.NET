using System.Text.Json;

using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.RabbitMq;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Contracts.IntegrationEvents;

namespace Forum.Api.Realtime;

/// <summary>
/// The hub's event catalog: which integration events fan out to sockets and how each becomes a
/// <see cref="RealtimeNotification"/>. Deliberately only the events the SPA's feed/thread views patch on —
/// Content's thread/comment lifecycle and Engagement's reactions. Identity events, <c>CategoryCreated</c> and
/// <c>FileCommitted</c> are consciously not wired (no live view consumes them yet); adding one later is a new
/// switch arm, not new machinery.
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
            ThreadCreatedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Created, ChangeNotification.ThreadEntity, e.ThreadId, null, e.CategoryId),
                e.CategoryId, e.ThreadId, ActorUserId: null),
            ThreadUpdatedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Updated, ChangeNotification.ThreadEntity, e.ThreadId, null, e.CategoryId),
                e.CategoryId, e.ThreadId, ActorUserId: null),
            ThreadDeletedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Deleted, ChangeNotification.ThreadEntity, e.ThreadId, null, e.CategoryId),
                e.CategoryId, e.ThreadId, ActorUserId: null),
            CommentCreatedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Created, ChangeNotification.CommentEntity, e.CommentId, e.ThreadId, e.CategoryId),
                e.CategoryId, e.ThreadId, ActorUserId: null),
            CommentUpdatedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Updated, ChangeNotification.CommentEntity, e.CommentId, e.ThreadId, e.CategoryId),
                e.CategoryId, e.ThreadId, ActorUserId: null),
            CommentDeletedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Deleted, ChangeNotification.CommentEntity, e.CommentId, e.ThreadId, e.CategoryId),
                e.CategoryId, e.ThreadId, ActorUserId: null),
            ReactionAddedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Created, ChangeNotification.ReactionEntity, e.TargetId, e.ThreadId, e.CategoryId),
                e.CategoryId, e.ThreadId, e.UserId),
            ReactionRemovedIntegrationEvent e => new RealtimeNotification(
                Envelope(ChangeNotification.Deleted, ChangeNotification.ReactionEntity, e.TargetId, e.ThreadId, e.CategoryId),
                e.CategoryId, e.ThreadId, e.UserId),
            _ => null,
        };
        return notification is not null;
    }

    private static ChangeNotification Envelope(string type, string entity, Ulid id, Ulid? parentId, Ulid categoryId) =>
        new(type, entity, id.ToString(), parentId?.ToString(), categoryId.ToString());
}
