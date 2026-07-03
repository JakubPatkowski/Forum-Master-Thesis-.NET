using Forum.Common.Messaging;

namespace Forum.Modules.Files.Contracts.IntegrationEvents;

/// <summary>
/// Published when an upload passed commit verification (real size/type checked, dimensions decoded).
/// Consumers (Phase 6+): Identity/Content can sync their denormalized avatar/icon pointers.
/// </summary>
public sealed record FileCommittedIntegrationEvent(
    Ulid EventId,
    Ulid FileId,
    Ulid UploadedBy,
    string ContentType,
    long SizeBytes,
    int Width,
    int Height,
    DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
