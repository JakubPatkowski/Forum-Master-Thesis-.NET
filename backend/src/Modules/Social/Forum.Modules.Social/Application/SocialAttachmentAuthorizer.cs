using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application;

/// <summary>
/// Implements <see cref="ISocialAuthorization"/> for Files (ContentAttachmentAuthorizer's mirror). Writes:
/// message images are the SENDER's alone (existence 404 → sender 403 → tombstone 422); a group icon needs the
/// owner or <c>moderate</c> at the group's ACL scope — resolved with the explicit-user
/// <see cref="IPermissionService"/> overload because Files passes the acting user in. Reads: message images
/// require an ACTIVE conversation seat (the read-gap fix — ULID unguessability is not an access model for private
/// chats); group icons are anonymous-readable like avatars.
/// </summary>
internal sealed class SocialAttachmentAuthorizer : ISocialAuthorization
{
    private readonly IConversationRepository _conversations;
    private readonly IGroupRepository _groups;
    private readonly IPermissionService _permissions;

    public SocialAttachmentAuthorizer(
        IConversationRepository conversations, IGroupRepository groups, IPermissionService permissions)
    {
        _conversations = conversations;
        _groups = groups;
        _permissions = permissions;
    }

    public async Task<Result> AuthorizeAttachmentAsync(
        SocialAttachmentTarget target, Ulid targetId, Ulid userId, CancellationToken cancellationToken)
    {
        switch (target)
        {
            case SocialAttachmentTarget.Message:
                {
                    var message = await _conversations.GetMessageAsync(targetId, cancellationToken);
                    if (message is null)
                    {
                        return Result.Failure(SocialErrors.MessageNotFound);
                    }

                    if (message.OwnerId != userId)
                    {
                        return Result.Failure(SocialErrors.NotMessageSender);
                    }

                    return message.IsDeleted
                        ? Result.Failure(Domain.Conversations.MessageErrors.Deleted)
                        : Result.Success();
                }

            case SocialAttachmentTarget.GroupIcon:
                {
                    var group = await _groups.GetByIdAsync(targetId, cancellationToken);
                    if (group is null)
                    {
                        return Result.Failure(SocialErrors.GroupNotFound);
                    }

                    if (group.OwnerId != userId
                        && !await _permissions.HasPermissionAsync(
                            userId, Permissions.Moderate, PermissionScopes.Group, group.Id, cancellationToken))
                    {
                        return Result.Failure(SocialErrors.GroupForbidden);
                    }

                    return Result.Success();
                }

            default:
                return Result.Failure(SocialErrors.GroupNotFound);
        }
    }

    public async Task<Result> AuthorizeFileReadAsync(
        SocialAttachmentTarget target, Ulid targetId, Ulid? userId, CancellationToken cancellationToken)
    {
        if (target != SocialAttachmentTarget.Message)
        {
            return Result.Success(); // Group icons: anonymous-readable, avatar parity.
        }

        var message = await _conversations.GetMessageAsync(targetId, cancellationToken);
        if (message is null)
        {
            return Result.Failure(SocialErrors.MessageNotFound);
        }

        if (userId is not { } readerId
            || await _conversations.GetParticipantAsync(
                message.ConversationId, readerId, cancellationToken) is not { IsActive: true })
        {
            return Result.Failure(SocialErrors.NotParticipant);
        }

        return Result.Success();
    }
}
