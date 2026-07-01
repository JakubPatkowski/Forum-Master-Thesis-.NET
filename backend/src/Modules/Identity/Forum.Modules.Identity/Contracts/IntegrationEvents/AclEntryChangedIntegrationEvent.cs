using Forum.Common.Messaging;

namespace Forum.Modules.Identity.Contracts.IntegrationEvents;

/// <summary>Published when an ACL entry changes. Enqueues an <c>effective_perm_cache</c> recompute for the user.</summary>
public sealed record AclEntryChangedIntegrationEvent(
    Ulid EventId, Ulid UserId, string Scope, Ulid? ScopeId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
