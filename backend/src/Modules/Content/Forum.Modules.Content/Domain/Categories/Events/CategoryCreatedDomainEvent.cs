using Forum.SharedKernel.Domain;

namespace Forum.Modules.Content.Domain.Categories.Events;

internal sealed record CategoryCreatedDomainEvent(
    Ulid CategoryId, string Slug, Ulid OwnerId, DateTimeOffset OccurredOnUtc) : IDomainEvent;
