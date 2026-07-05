using Forum.Common.Messaging;

namespace Forum.Modules.Engagement.Contracts.IntegrationEvents;

/// <summary>
/// Published when a user reacts to a target. Target type and reaction type carry their wire names; the owning
/// category and containing thread (the thread itself for thread targets) let the WebSocket hub scope the push.
/// </summary>
public sealed record ReactionAddedIntegrationEvent(
    Ulid EventId,
    Ulid UserId,
    string TargetType,
    Ulid TargetId,
    string ReactionType,
    Ulid CategoryId,
    Ulid ThreadId,
    DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
