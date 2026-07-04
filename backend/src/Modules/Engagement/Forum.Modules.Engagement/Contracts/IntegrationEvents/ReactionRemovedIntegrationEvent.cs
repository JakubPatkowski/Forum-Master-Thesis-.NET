using Forum.Common.Messaging;

namespace Forum.Modules.Engagement.Contracts.IntegrationEvents;

/// <summary>Published when a user withdraws a reaction from a target.</summary>
public sealed record ReactionRemovedIntegrationEvent(
    Ulid EventId,
    Ulid UserId,
    string TargetType,
    Ulid TargetId,
    string ReactionType,
    DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
