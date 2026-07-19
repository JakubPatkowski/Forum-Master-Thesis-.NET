using Forum.Common.Messaging;

namespace Forum.Modules.Social.Contracts.IntegrationEvents;

/// <summary>
/// Published when a durable notification row is created for a user. The realtime push built from this is pure
/// identity + routing (ADR 0010): the client re-fetches its notification list/count — kind, actor and target stay
/// in the database row, never in the socket payload.
/// </summary>
public sealed record NotificationCreatedIntegrationEvent(
    Ulid EventId, Ulid NotificationId, Ulid UserId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
