using Forum.SharedKernel.Domain;

namespace Forum.Modules.Content.Domain.Threads.Events;

internal sealed record ThreadCreatedDomainEvent(
    Ulid ThreadId, Ulid CategoryId, Ulid OwnerId, string Title, DateTimeOffset OccurredOnUtc) : IDomainEvent;
