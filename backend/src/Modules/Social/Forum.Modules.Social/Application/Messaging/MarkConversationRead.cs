using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Messaging;

/// <summary>
/// Stamps the caller's own read marker (their unread badge zeroes). This is the OWNER's marker only — no
/// read-receipt ever reaches the sender (REQUIREMENTS §1), and no event is published (multi-device read sync is
/// a documented non-goal; the other devices catch up on their next fetch).
/// </summary>
internal sealed record MarkConversationReadCommand(Ulid ConversationId) : ICommand;

internal sealed class MarkConversationReadCommandHandler : ICommandHandler<MarkConversationReadCommand>
{
    private readonly IConversationRepository _conversations;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public MarkConversationReadCommandHandler(
        IConversationRepository conversations,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _conversations = conversations;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(MarkConversationReadCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var conversation = await _conversations.GetByIdAsync(command.ConversationId, cancellationToken);
        if (conversation is null)
        {
            return Result.Failure(SocialErrors.ConversationNotFound);
        }

        var seat = await _conversations.GetParticipantAsync(conversation.Id, userId, cancellationToken);
        if (seat is not { IsActive: true })
        {
            return Result.Failure(SocialErrors.NotParticipant);
        }

        seat.MarkRead(_clock.GetUtcNow());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
