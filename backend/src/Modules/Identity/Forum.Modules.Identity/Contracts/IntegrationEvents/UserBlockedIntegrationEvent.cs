using Forum.Common.Messaging;

namespace Forum.Modules.Identity.Contracts.IntegrationEvents;

/// <summary>Published when an account is blocked. Consumed by Content/Social to hide or disable the user's actions.</summary>
public sealed record UserBlockedIntegrationEvent(
    Ulid EventId, Ulid UserId, Ulid BlockedBy, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
