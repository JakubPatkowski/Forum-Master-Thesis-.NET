using Forum.SharedKernel.Domain;

namespace Forum.Modules.Content.Domain.Comments.Events;

internal sealed record CommentDeletedDomainEvent(
    Ulid CommentId, Ulid ThreadId, Ulid DeletedBy, DateTimeOffset OccurredOnUtc) : IDomainEvent;
