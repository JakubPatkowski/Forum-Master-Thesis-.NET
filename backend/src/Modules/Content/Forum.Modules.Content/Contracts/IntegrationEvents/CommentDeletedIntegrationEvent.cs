using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>
/// Published when a comment is soft-deleted. Consumed by Files (detach attachments), Engagement (drop
/// reactions) and the WebSocket hub (which scopes the push by the thread's category).
/// </summary>
public sealed record CommentDeletedIntegrationEvent(
    Ulid EventId, Ulid CommentId, Ulid ThreadId, Ulid CategoryId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
