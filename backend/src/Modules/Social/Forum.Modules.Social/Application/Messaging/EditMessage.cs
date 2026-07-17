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
/// Edits a message body (sender-only — moderation removes, it never rewrites, so edit stays strictly personal).
/// Order: 404 → sender 403 → tombstone/body 422. Clients show the edited marker off <c>EditedOnUtc</c>.
/// </summary>
internal sealed record EditMessageCommand(Ulid MessageId, string Body) : ICommand;

internal sealed class EditMessageCommandValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageCommandValidator() =>
        RuleFor(static command => command.Body).NotEmpty().MaximumLength(Message.MaxBodyLength);
}

internal sealed class EditMessageCommandHandler : ICommandHandler<EditMessageCommand>
{
    private readonly IValidator<EditMessageCommand> _validator;
    private readonly IConversationRepository _conversations;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public EditMessageCommandHandler(
        IValidator<EditMessageCommand> validator,
        IConversationRepository conversations,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _conversations = conversations;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(EditMessageCommand command, CancellationToken cancellationToken)
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

        if (message.OwnerId != userId)
        {
            return Result.Failure(SocialErrors.NotMessageSender);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure(validationError);
        }

        var now = _clock.GetUtcNow();
        var edited = message.Edit(command.Body, now);
        if (edited.IsFailure)
        {
            return edited;
        }

        var conversation = await _conversations.GetByIdAsync(message.ConversationId, cancellationToken);
        if (conversation is not null)
        {
            var participants = await _conversations.GetActiveParticipantsAsync(conversation.Id, cancellationToken);
            _outbox.Enqueue(new MessageEditedIntegrationEvent(
                Ulid.NewUlid(), message.Id, conversation.Id, ConversationWire.TypeOf(conversation), userId,
                ConversationWire.DirectParticipants(conversation, participants), now));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
