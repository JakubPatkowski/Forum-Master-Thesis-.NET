using Forum.SharedKernel.Domain;

namespace Forum.Modules.Content.Domain.Threads.Events;

internal sealed record ThreadDeletedDomainEvent(
    Ulid ThreadId, Ulid DeletedBy, DateTimeOffset OccurredOnUtc) : IDomainEvent;
