using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Groups;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Conversations;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Messaging;

/// <summary>
/// Tombstones a message (Comment.Delete precedent: the row stays, the body reads "[deleted]"). The sender always
/// may; in a GROUP chat the group's manage-gate (owner / group admin / staff) may too — that is the one place
/// staff touch chat content, and it removes, never reads. DMs are sender-only. Files detaches the message's
/// images off the integration event.
/// </summary>
internal sealed record DeleteMessageCommand(Ulid MessageId) : ICommand;

internal sealed class DeleteMessageCommandHandler : ICommandHandler<DeleteMessageCommand>
{
    private readonly IConversationRepository _conversations;
    private readonly IGroupRepository _groups;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public DeleteMessageCommandHandler(
        IConversationRepository conversations,
        IGroupRepository groups,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _conversations = conversations;
        _groups = groups;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(DeleteMessageCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var message = await _conversations.GetMessageAsync(command.MessageId, cancellationToken);
        if (message is null)
        {
            return Result.Failure(SocialErrors.MessageNotFound);
        }

        var conversation = await _conversations.GetByIdAsync(message.ConversationId, cancellationToken);
        if (conversation is null)
        {
            return Result.Failure(SocialErrors.MessageNotFound);
        }

        if (message.OwnerId != userId && !await MayModerateAsync(conversation, cancellationToken))
        {
            return Result.Failure(SocialErrors.NotMessageSender);
        }

        var now = _clock.GetUtcNow();
        var deleted = message.Delete(userId, now);
        if (deleted.IsFailure)
        {
            return deleted;
        }

        var participants = await _conversations.GetActiveParticipantsAsync(conversation.Id, cancellationToken);
        _outbox.Enqueue(new MessageDeletedIntegrationEvent(
            Ulid.NewUlid(), message.Id, conversation.Id, ConversationWire.TypeOf(conversation), message.OwnerId,
            ConversationWire.DirectParticipants(conversation, participants), now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<bool> MayModerateAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        if (conversation.Type != ConversationType.Group)
        {
            return false;
        }

        // Group conversation id == group id; a soft-deleted group resolves null → sender-only.
        var group = await _groups.GetByIdAsync(conversation.Id, cancellationToken);
        return group is not null && await GroupGuards.MayManageAsync(_currentUser, group, cancellationToken);
    }
}
