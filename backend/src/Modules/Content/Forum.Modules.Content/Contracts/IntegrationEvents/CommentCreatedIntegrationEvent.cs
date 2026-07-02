using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a comment is created.</summary>
public sealed record CommentCreatedIntegrationEvent(
    Ulid EventId, Ulid CommentId, Ulid ThreadId, Ulid? ParentId, Ulid OwnerId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
