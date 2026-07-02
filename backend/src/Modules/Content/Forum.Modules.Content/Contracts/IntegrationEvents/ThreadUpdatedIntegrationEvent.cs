using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>Published when a thread's title or body changes (drives WebSocket fetch-then-patch in Phase 7).</summary>
public sealed record ThreadUpdatedIntegrationEvent(
    Ulid EventId, Ulid ThreadId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
