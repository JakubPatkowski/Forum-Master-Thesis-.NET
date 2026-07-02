using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a comment is soft-deleted. Consumed by Files (detach attachments) and Engagement.</summary>
public sealed record CommentDeletedIntegrationEvent(
    Ulid EventId, Ulid CommentId, Ulid ThreadId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
