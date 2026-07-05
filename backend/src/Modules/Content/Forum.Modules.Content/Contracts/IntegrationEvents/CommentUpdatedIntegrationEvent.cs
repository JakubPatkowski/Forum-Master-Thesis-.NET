using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a comment's body changes (drives the WebSocket fetch-then-patch of the comment tree).</summary>
public sealed record CommentUpdatedIntegrationEvent(
    Ulid EventId, Ulid CommentId, Ulid ThreadId, Ulid CategoryId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
