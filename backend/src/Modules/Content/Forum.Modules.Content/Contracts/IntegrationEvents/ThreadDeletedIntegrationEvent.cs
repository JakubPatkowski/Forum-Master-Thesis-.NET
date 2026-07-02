using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a thread is soft-deleted. Consumed by Files (detach attachments) and Engagement.</summary>
public sealed record ThreadDeletedIntegrationEvent(
    Ulid EventId, Ulid ThreadId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
