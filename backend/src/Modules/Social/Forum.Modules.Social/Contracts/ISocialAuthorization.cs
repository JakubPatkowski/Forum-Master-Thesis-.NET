using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Contracts;

/// <summary>A Social-owned object a file can be attached to.</summary>
public enum SocialAttachmentTarget
{
    /// <summary>A chat message (DM or group chat) — inline images.</summary>
    Message,

    /// <summary>A group's icon — replace semantics, like avatars and category icons.</summary>
    GroupIcon,
}

/// <summary>
/// Social's authorization surface for the Files module (the <c>IContentAuthorization</c> mirror — same acyclic
/// direction: Files → Social, never back). Write side: may this user attach/detach a file on this social object?
/// Read side closes the gap Content's public images deliberately accept: a private chat image must not be readable
/// by ULID guessing, so message-target reads require an active conversation participant; group icons stay
/// anonymous-readable (avatar parity — groups are discoverable).
/// </summary>
public interface ISocialAuthorization
{
    /// <summary>
    /// Success when <paramref name="userId"/> may modify the target's attachments; otherwise the same
    /// NotFound/Forbidden errors Social's own write handlers would return (404 → 403 order preserved).
    /// Message: only its sender. GroupIcon: the group's owner or <c>moderate</c> at the group's scope.
    /// </summary>
    Task<Result> AuthorizeAttachmentAsync(
        SocialAttachmentTarget target, Ulid targetId, Ulid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Success when <paramref name="userId"/> may read files attached to the target. Message targets require an
    /// active participant (anonymous → Forbidden); GroupIcon always succeeds.
    /// </summary>
    Task<Result> AuthorizeFileReadAsync(
        SocialAttachmentTarget target, Ulid targetId, Ulid? userId, CancellationToken cancellationToken);
}
