using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a category is created.</summary>
public sealed record CategoryCreatedIntegrationEvent(
    Ulid EventId, Ulid CategoryId, string Slug, Ulid OwnerId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
