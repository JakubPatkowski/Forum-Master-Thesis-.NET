using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a comment is created. The category is the thread's (WebSocket push scoping).</summary>
public sealed record CommentCreatedIntegrationEvent(
    Ulid EventId,
    Ulid CommentId,
    Ulid ThreadId,
    Ulid? ParentId,
    Ulid OwnerId,
    Ulid CategoryId,
    DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
