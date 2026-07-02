using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a thread is created. Consumed by Files/Engagement/Notifications.</summary>
public sealed record ThreadCreatedIntegrationEvent(
    Ulid EventId, Ulid ThreadId, Ulid CategoryId, Ulid OwnerId, string Title, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
