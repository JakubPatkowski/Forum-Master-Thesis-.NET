using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Application.Validation;
using Forum.Modules.Files.Domain.Files;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Files.Application.Files;

/// <summary>
/// Step 1 of the direct-to-MinIO flow (ADR 0008): any authenticated user may initiate — a pending,
/// unattached file is inert, so no ACL action gates it. The declared content type/size are policy-checked
/// here but stay untrusted until commit re-verifies them against the stored object.
/// </summary>
internal sealed record InitiateUploadCommand(string ContentType, long SizeBytes) : ICommand<InitiateUploadResponse>;

internal sealed record InitiateUploadResponse(
    Ulid FileId, string ObjectKey, string UploadUrl, DateTimeOffset ExpiresOnUtc);

internal sealed class InitiateUploadCommandValidator : AbstractValidator<InitiateUploadCommand>
{
    public InitiateUploadCommandValidator()
    {
        RuleFor(static command => command.ContentType).NotEmpty().MaximumLength(128);
        RuleFor(static command => command.SizeBytes).GreaterThan(0);
    }
}

internal sealed class InitiateUploadCommandHandler : ICommandHandler<InitiateUploadCommand, InitiateUploadResponse>
{
    private readonly IValidator<InitiateUploadCommand> _validator;
    private readonly IStoredFileRepository _files;
    private readonly IObjectStorage _storage;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly FilesOptions _options;
    private readonly StorageOptions _storageOptions;

    public InitiateUploadCommandHandler(
        IValidator<InitiateUploadCommand> validator,
        IStoredFileRepository files,
        IObjectStorage storage,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IOptions<FilesOptions> options,
        IOptions<StorageOptions> storageOptions)
    {
        _validator = validator;
        _files = files;
        _storage = storage;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
        _storageOptions = storageOptions.Value;
    }

    public async Task<Result<InitiateUploadResponse>> Handle(
        InitiateUploadCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } ownerId)
        {
            return Result.Failure<InitiateUploadResponse>(FilesErrors.AuthenticationRequired);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<InitiateUploadResponse>(validationError);
        }

        var declaredType = command.ContentType.Trim().ToLowerInvariant();
        if (!_options.AllowedContentTypes.Contains(declaredType, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure<InitiateUploadResponse>(FileErrors.ContentTypeNotAllowed);
        }

        if (command.SizeBytes > _options.MaxSizeBytes)
        {
            return Result.Failure<InitiateUploadResponse>(FileErrors.TooLarge);
        }

        var file = StoredFile.Create(_storageOptions.Bucket, declaredType, command.SizeBytes, ownerId);
        _files.Add(file);

        var ttl = TimeSpan.FromMinutes(_options.UploadUrlTtlMinutes);
        var uploadUrl = await _storage.PresignPutAsync(file.ObjectKey, ttl, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new InitiateUploadResponse(
            file.Id, file.ObjectKey, uploadUrl, _clock.GetUtcNow().Add(ttl)));
    }
}
