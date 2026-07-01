using Forum.Common.Messaging;

namespace Forum.Modules.Identity.Contracts.IntegrationEvents;

/// <summary>Published when a new account is created. Consumed by Files (avatar bootstrap) and others.</summary>
public sealed record UserRegisteredIntegrationEvent(
    Ulid EventId, Ulid UserId, string Username, string Email, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
