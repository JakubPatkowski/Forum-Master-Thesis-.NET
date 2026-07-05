using Forum.Common.Messaging;

namespace Forum.Modules.Content.Contracts.IntegrationEvents;

/// <summary>
/// Published when a thread changes after creation (title/body edit, pin toggle, category move). Carries the
/// thread's current category so the WebSocket hub can scope and authorize the push without a lookup.
/// </summary>
public sealed record ThreadUpdatedIntegrationEvent(
    Ulid EventId, Ulid ThreadId, Ulid CategoryId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
