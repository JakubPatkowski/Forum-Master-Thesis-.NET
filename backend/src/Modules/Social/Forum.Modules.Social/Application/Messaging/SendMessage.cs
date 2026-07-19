using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Validation;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Conversations;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Messaging;

/// <summary>
/// Sends a message. Order: conversation 404 → active seat 403 → (direct only) block/privacy re-check 403, so a
/// block or tightened setting gates the NEXT message even inside an old conversation → body 422. The sender's own
/// read marker advances with their message; the integration event carries routing facts only (ADR 0010).
/// </summary>
internal sealed record SendMessageCommand(Ulid ConversationId, string Body) : ICommand<SendMessageResponse>;

internal sealed record SendMessageResponse(Ulid MessageId, DateTimeOffset SentOnUtc);

internal sealed class SendMessageCommandValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageCommandValidator() =>
        RuleFor(static command => command.Body).NotEmpty().MaximumLength(Message.MaxBodyLength);
}

internal sealed class SendMessageCommandHandler : ICommandHandler<SendMessageCommand, SendMessageResponse>
{
    private readonly IValidator<SendMessageCommand> _validator;
    private readonly IConversationRepository _conversations;
    private readonly SocialInteractionGate _gate;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public SendMessageCommandHandler(
        IValidator<SendMessageCommand> validator,
        IConversationRepository conversations,
        SocialInteractionGate gate,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _conversations = conversations;
        _gate = gate;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SendMessageResponse>> Handle(
        SendMessageCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<SendMessageResponse>(SocialErrors.AuthenticationRequired);
        }

        var conversation = await _conversations.GetByIdAsync(command.ConversationId, cancellationToken);
        if (conversation is null)
        {
            return Result.Failure<SendMessageResponse>(SocialErrors.ConversationNotFound);
        }

        var seat = await _conversations.GetParticipantAsync(conversation.Id, userId, cancellationToken);
        if (seat is not { IsActive: true })
        {
            return Result.Failure<SendMessageResponse>(SocialErrors.NotParticipant);
        }

        var participants = await _conversations.GetActiveParticipantsAsync(conversation.Id, cancellationToken);
        if (conversation.Type == ConversationType.Direct)
        {
            var other = participants.FirstOrDefault(participant => participant.UserId != userId);
            if (other is not null)
            {
                var allowed = await _gate.MayMessageAsync(userId, other.UserId, cancellationToken);
                if (allowed.IsFailure)
                {
                    return Result.Failure<SendMessageResponse>(allowed.Error);
                }
            }
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<SendMessageResponse>(validationError);
        }

        var now = _clock.GetUtcNow();
        var message = Message.Create(conversation.Id, userId, command.Body);
        _conversations.AddMessage(message);
        seat.MarkRead(now); // Your own message never counts against your unread badge.

        _outbox.Enqueue(new MessageSentIntegrationEvent(
            Ulid.NewUlid(), message.Id, conversation.Id, ConversationWire.TypeOf(conversation), userId,
            ConversationWire.DirectParticipants(conversation, participants), now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new SendMessageResponse(message.Id, now));
    }
}
