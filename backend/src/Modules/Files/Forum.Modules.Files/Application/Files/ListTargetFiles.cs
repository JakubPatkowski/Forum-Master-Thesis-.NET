using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Social.Contracts;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Files;

/// <summary>
/// Lists the committed files attached to one target (e.g. a thread's images), each with a presigned GET URL.
/// Bounded by the per-target attachment cap, so no cursor is needed. Message targets are participant-gated
/// through Social (the read-side counterpart of the attach gate); everything else stays anonymous like the
/// public forum reads.
/// </summary>
internal sealed record ListTargetFilesQuery(string TargetType, Ulid TargetId)
    : IQuery<IReadOnlyList<FileDownloadResponse>>;

internal sealed class ListTargetFilesQueryHandler
    : IQueryHandler<ListTargetFilesQuery, IReadOnlyList<FileDownloadResponse>>
{
    private readonly IFilesQueries _queries;
    private readonly ISocialAuthorization _socialAuthorization;
    private readonly ICurrentUser _currentUser;
    private readonly IObjectStorage _storage;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;

    public ListTargetFilesQueryHandler(
        IFilesQueries queries,
        ISocialAuthorization socialAuthorization,
        ICurrentUser currentUser,
        IObjectStorage storage,
        TimeProvider clock,
        IOptions<FilesOptions> options)
    {
        _queries = queries;
        _socialAuthorization = socialAuthorization;
        _currentUser = currentUser;
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

        if (targetType == FileTargetType.Message)
        {
            var readable = await _socialAuthorization.AuthorizeFileReadAsync(
                SocialAttachmentTarget.Message, query.TargetId, _currentUser.Id, cancellationToken);
            if (readable.IsFailure)
            {
                return Result.Failure<IReadOnlyList<FileDownloadResponse>>(readable.Error);
            }
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
