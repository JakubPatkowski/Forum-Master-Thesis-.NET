using Forum.Common.Messaging;

namespace Forum.Modules.Files.Contracts.IntegrationEvents;

/// <summary>
/// Published when the orphan sweep physically removed a file: either a pending upload that outlived its grace
/// window ("pending_expired") or a committed blob left with no attachment ("unattached").
/// </summary>
public sealed record FileOrphanedIntegrationEvent(
    Ulid EventId,
    Ulid FileId,
    Ulid UploadedBy,
    string ObjectKey,
    string Reason,
    DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
