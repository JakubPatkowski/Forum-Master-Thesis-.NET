using Forum.Common.Cqrs;
using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Files;

/// <summary>
/// Serves a committed file as a short-lived presigned GET — the backend stays out of the byte path (ADR 0008).
/// Pending files 404: until commit they don't exist as content. Anonymous access mirrors Content's public reads;
/// the unguessable ULID plus the short TTL bound exposure.
/// </summary>
internal sealed record GetFileDownloadQuery(Ulid FileId) : IQuery<FileDownloadResponse>;

internal sealed record FileDownloadResponse(
    Ulid FileId, string Url, string ContentType, long SizeBytes, int? Width, int? Height, DateTimeOffset ExpiresOnUtc);

internal sealed class GetFileDownloadQueryHandler : IQueryHandler<GetFileDownloadQuery, FileDownloadResponse>
{
    private readonly IFilesQueries _queries;
    private readonly IObjectStorage _storage;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;

    public GetFileDownloadQueryHandler(
        IFilesQueries queries, IObjectStorage storage, TimeProvider clock, IOptions<FilesOptions> options)
    {
        _queries = queries;
        _storage = storage;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<FileDownloadResponse>> Handle(
        GetFileDownloadQuery query, CancellationToken cancellationToken)
    {
        var file = await _queries.GetCommittedAsync(query.FileId, cancellationToken);
        if (file is null)
        {
            return Result.Failure<FileDownloadResponse>(FileErrors.NotFound);
        }

        var ttl = TimeSpan.FromMinutes(_options.DownloadUrlTtlMinutes);
        var url = await _storage.PresignGetAsync(file.ObjectKey, ttl, cancellationToken);

        return Result.Success(new FileDownloadResponse(
            file.Id, url, file.ContentType, file.SizeBytes, file.Width, file.Height, _clock.GetUtcNow().Add(ttl)));
    }
}
