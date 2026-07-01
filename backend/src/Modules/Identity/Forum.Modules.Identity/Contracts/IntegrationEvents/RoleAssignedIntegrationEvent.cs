using Forum.Common.Messaging;

namespace Forum.Modules.Identity.Contracts.IntegrationEvents;

/// <summary>Published when a user's role changes. Enqueues an <c>effective_perm_cache</c> recompute for the user.</summary>
public sealed record RoleAssignedIntegrationEvent(
    Ulid EventId, Ulid UserId, Ulid RoleId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
