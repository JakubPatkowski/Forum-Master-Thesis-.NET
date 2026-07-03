using Forum.Modules.Files.Domain.Files;

namespace Forum.Modules.Files.Application.Abstractions;

/// <summary>Read model of a committed file (the URL is presigned separately by the handler).</summary>
internal sealed record FileReadModel(
    Ulid Id, string ObjectKey, string ContentType, long SizeBytes, int? Width, int? Height);

/// <summary>
/// The Files read side. Single-module, bounded lookups (a target carries at most the attachment cap), so
/// plain no-tracking EF suffices — no view or keyset cursor needed (same precedent as Content's category list).
/// </summary>
internal interface IFilesQueries
{
    Task<FileReadModel?> GetCommittedAsync(Ulid fileId, CancellationToken cancellationToken);

    Task<IReadOnlyList<FileReadModel>> ListCommittedForTargetAsync(
        FileTargetType targetType, Ulid targetId, CancellationToken cancellationToken);
}
