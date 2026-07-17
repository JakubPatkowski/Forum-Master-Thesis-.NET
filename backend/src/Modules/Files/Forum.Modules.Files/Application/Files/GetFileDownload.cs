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
/// Serves a committed file as a short-lived presigned GET — the backend stays out of the byte path (ADR 0008).
/// Pending files 404: until commit they don't exist as content. Anonymous access mirrors Content's public reads
/// (unguessable ULID + short TTL bound exposure) — EXCEPT files attached to a chat message: a private
/// conversation's images are participant-gated through Social, because "hard to guess" is not an access model
/// for private chats. A message-attached file that also hangs off a public target stays gated (private wins).
/// </summary>
internal sealed record GetFileDownloadQuery(Ulid FileId) : IQuery<FileDownloadResponse>;

internal sealed record FileDownloadResponse(
    Ulid FileId, string Url, string ContentType, long SizeBytes, int? Width, int? Height, DateTimeOffset ExpiresOnUtc);

internal sealed class GetFileDownloadQueryHandler : IQueryHandler<GetFileDownloadQuery, FileDownloadResponse>
{
    private readonly IFilesQueries _queries;
    private readonly ISocialAuthorization _socialAuthorization;
    private readonly ICurrentUser _currentUser;
    private readonly IObjectStorage _storage;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;

    public GetFileDownloadQueryHandler(
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

    public async Task<Result<FileDownloadResponse>> Handle(
        GetFileDownloadQuery query, CancellationToken cancellationToken)
    {
        var file = await _queries.GetCommittedAsync(query.FileId, cancellationToken);
        if (file is null)
        {
            return Result.Failure<FileDownloadResponse>(FileErrors.NotFound);
        }

        var attachments = await _queries.GetAttachmentRefsAsync(file.Id, cancellationToken);
        foreach (var attachment in attachments)
        {
            if (attachment.TargetType != FileTargetType.Message)
            {
                continue;
            }

            var readable = await _socialAuthorization.AuthorizeFileReadAsync(
                SocialAttachmentTarget.Message, attachment.TargetId, _currentUser.Id, cancellationToken);
            if (readable.IsFailure)
            {
                return Result.Failure<FileDownloadResponse>(readable.Error);
            }
        }

        var ttl = TimeSpan.FromMinutes(_options.DownloadUrlTtlMinutes);
        var url = await _storage.PresignGetAsync(file.ObjectKey, ttl, cancellationToken);

        return Result.Success(new FileDownloadResponse(
            file.Id, url, file.ContentType, file.SizeBytes, file.Width, file.Height, _clock.GetUtcNow().Add(ttl)));
    }
}
