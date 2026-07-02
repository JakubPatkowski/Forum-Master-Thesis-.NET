using Forum.SharedKernel.Domain;

namespace Forum.Modules.Content.Domain.Comments.Events;

internal sealed record CommentCreatedDomainEvent(
    Ulid CommentId, Ulid ThreadId, Ulid? ParentId, Ulid OwnerId, DateTimeOffset OccurredOnUtc) : IDomainEvent;
