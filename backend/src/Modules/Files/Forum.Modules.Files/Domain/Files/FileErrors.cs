using Forum.SharedKernel.Results;

namespace Forum.Modules.Files.Domain.Files;

/// <summary>Typed errors for the file lifecycle. No exceptions for expected failures.</summary>
internal static class FileErrors
{
    public static readonly Error NotFound = Error.NotFound("file.not_found", "File not found.");

    public static readonly Error NotOwner = Error.Forbidden(
        "file.not_owner", "Only the user who initiated the upload may act on this file.");

    public static readonly Error NotUploaded = Error.Conflict(
        "file.not_uploaded", "No object was uploaded for this file yet — PUT the bytes to the presigned URL first.");

    public static readonly Error AlreadyCommitted = Error.Conflict(
        "file.already_committed", "The file is already committed.");

    public static readonly Error NotCommitted = Error.Conflict(
        "file.not_committed", "The file must be committed before it can be attached.");

    public static readonly Error SizeMismatch = Error.Validation(
        "file.size_mismatch", "The uploaded object's size does not match the declared size.");

    public static readonly Error TypeMismatch = Error.Validation(
        "file.type_mismatch", "The uploaded object's real content type does not match the declared content type.");

    public static readonly Error NotADecodableImage = Error.Validation(
        "file.not_an_image", "The uploaded object is not a decodable image of an allowed format.");

    public static readonly Error ContentTypeNotAllowed = Error.Validation(
        "file.content_type_not_allowed", "The declared content type is not in the allowed list.");

    public static readonly Error TooLarge = Error.Validation(
        "file.too_large", "The declared size exceeds the maximum allowed upload size.");

    public static readonly Error InvalidTargetType = Error.Validation(
        "file.invalid_target_type", "Unknown attachment target type.");

    public static readonly Error TargetRequired = Error.Validation(
        "file.target_required", "This target type requires a target id.");

    public static readonly Error DmAttachmentsNotSupported = Error.Validation(
        "file.dm_not_supported", "Direct-message attachments arrive with the Social module (Phase 5).");

    public static readonly Error AvatarTargetMismatch = Error.Forbidden(
        "file.avatar_target_mismatch", "An avatar can only be attached to the requesting user.");

    public static readonly Error TooManyAttachments = Error.Validation(
        "file.too_many_attachments", "The target already carries the maximum number of attachments.");
}
