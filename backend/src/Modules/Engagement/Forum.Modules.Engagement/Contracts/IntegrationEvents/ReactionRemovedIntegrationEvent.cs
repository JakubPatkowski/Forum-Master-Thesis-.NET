using Forum.Common.Messaging;

namespace Forum.Modules.Engagement.Contracts.IntegrationEvents;

/// <summary>
/// Published when a user withdraws a reaction from a target. Carries the same routing fields as
/// <see cref="ReactionAddedIntegrationEvent"/> so the WebSocket hub can scope the push.
/// </summary>
public sealed record ReactionRemovedIntegrationEvent(
    Ulid EventId,
    Ulid UserId,
    string TargetType,
    Ulid TargetId,
    string ReactionType,
    Ulid CategoryId,
    Ulid ThreadId,
    DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
