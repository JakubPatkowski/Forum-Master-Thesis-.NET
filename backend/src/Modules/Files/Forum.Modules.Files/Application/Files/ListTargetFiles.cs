using Forum.Common.Cqrs;
using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Files;

/// <summary>
/// Lists the committed files attached to one target (e.g. a thread's images), each with a presigned GET URL.
/// Bounded by the per-target attachment cap, so no cursor is needed.
/// </summary>
internal sealed record ListTargetFilesQuery(string TargetType, Ulid TargetId)
    : IQuery<IReadOnlyList<FileDownloadResponse>>;

internal sealed class ListTargetFilesQueryHandler
    : IQueryHandler<ListTargetFilesQuery, IReadOnlyList<FileDownloadResponse>>
{
    private readonly IFilesQueries _queries;
    private readonly IObjectStorage _storage;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;

    public ListTargetFilesQueryHandler(
        IFilesQueries queries, IObjectStorage storage, TimeProvider clock, IOptions<FilesOptions> options)
    {
        _queries = queries;
        _storage = storage;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<IReadOnlyList<FileDownloadResponse>>> Handle(
        ListTargetFilesQuery query, CancellationToken cancellationToken)
    {
        if (!FileTargets.TryParse(query.TargetType, out var targetType))
        {
            return Result.Failure<IReadOnlyList<FileDownloadResponse>>(FileErrors.InvalidTargetType);
        }

        var files = await _queries.ListCommittedForTargetAsync(targetType, query.TargetId, cancellationToken);

        var ttl = TimeSpan.FromMinutes(_options.DownloadUrlTtlMinutes);
        var expiresOnUtc = _clock.GetUtcNow().Add(ttl);
        var responses = new List<FileDownloadResponse>(files.Count);
        foreach (var file in files)
        {
            var url = await _storage.PresignGetAsync(file.ObjectKey, ttl, cancellationToken);
            responses.Add(new FileDownloadResponse(
                file.Id, url, file.ContentType, file.SizeBytes, file.Width, file.Height, expiresOnUtc));
        }

        return Result.Success<IReadOnlyList<FileDownloadResponse>>(responses);
    }
}
