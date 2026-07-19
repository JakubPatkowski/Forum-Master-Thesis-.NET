using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Conversations;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Messaging;

/// <summary>
/// Get-or-creates the pair's Direct conversation (opened lazily from the chat UI — never pre-created per
/// friendship). Order: other user 404 → block/privacy 403 → self 422. The canonical <c>direct_key</c> unique
/// index closes the create race: the loser of a concurrent open re-reads the winner's row.
/// </summary>
internal sealed record OpenDirectConversationCommand(Ulid UserId) : ICommand<OpenDirectConversationResponse>;

internal sealed record OpenDirectConversationResponse(Ulid ConversationId);

internal sealed class OpenDirectConversationCommandHandler
    : ICommandHandler<OpenDirectConversationCommand, OpenDirectConversationResponse>
{
    private readonly IConversationRepository _conversations;
    private readonly IUserReader _users;
    private readonly SocialInteractionGate _gate;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public OpenDirectConversationCommandHandler(
        IConversationRepository conversations,
        IUserReader users,
        SocialInteractionGate gate,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _conversations = conversations;
        _users = users;
        _gate = gate;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<OpenDirectConversationResponse>> Handle(
        OpenDirectConversationCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<OpenDirectConversationResponse>(SocialErrors.AuthenticationRequired);
        }

        if (!await _users.IsActiveAsync(command.UserId, cancellationToken))
        {
            return Result.Failure<OpenDirectConversationResponse>(SocialErrors.UserNotFound);
        }

        if (userId == command.UserId)
        {
            return Result.Failure<OpenDirectConversationResponse>(SocialErrors.SelfConversation);
        }

        var allowed = await _gate.MayMessageAsync(userId, command.UserId, cancellationToken);
        if (allowed.IsFailure)
        {
            return Result.Failure<OpenDirectConversationResponse>(allowed.Error);
        }

        var key = Conversation.BuildDirectKey(userId, command.UserId);
        var existing = await _conversations.GetDirectByKeyAsync(key, cancellationToken);
        if (existing is not null)
        {
            return Result.Success(new OpenDirectConversationResponse(existing.Id));
        }

        var now = _clock.GetUtcNow();
        var conversation = Conversation.CreateDirect(userId, command.UserId);
        _conversations.Add(conversation);
        _conversations.AddParticipant(new ConversationParticipant(conversation.Id, userId, now));
        _conversations.AddParticipant(new ConversationParticipant(conversation.Id, command.UserId, now));

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            // Concurrent open won the direct_key unique race — theirs is THE conversation for this pair.
            var winner = await _conversations.GetDirectByKeyAsync(key, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return Result.Success(new OpenDirectConversationResponse(winner.Id));
        }

        return Result.Success(new OpenDirectConversationResponse(conversation.Id));
    }
}
