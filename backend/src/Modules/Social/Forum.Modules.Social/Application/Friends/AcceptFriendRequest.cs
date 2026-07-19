using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Notifications;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Friends;

/// <summary>
/// Accepts a pending request. A request not involving the caller reads as 404 (its existence is not their
/// business); the requester trying to accept their own reads as 403; a non-pending row is 422.
/// </summary>
internal sealed record AcceptFriendRequestCommand(Ulid FriendshipId) : ICommand;

internal sealed class AcceptFriendRequestCommandHandler : ICommandHandler<AcceptFriendRequestCommand>
{
    private readonly IFriendshipRepository _friendships;
    private readonly Notifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public AcceptFriendRequestCommandHandler(
        IFriendshipRepository friendships,
        Notifier notifier,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _friendships = friendships;
        _notifier = notifier;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(AcceptFriendRequestCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var friendship = await _friendships.GetByIdAsync(command.FriendshipId, cancellationToken);
        if (friendship is null || !friendship.Involves(userId))
        {
            return Result.Failure(SocialErrors.FriendshipNotFound);
        }

        var now = _clock.GetUtcNow();
        var accepted = friendship.Accept(userId, now);
        if (accepted.IsFailure)
        {
            return accepted;
        }

        _notifier.Notify(friendship.RequesterId, NotificationKinds.FriendAccepted, userId, friendship.Id, now);
        _outbox.Enqueue(new FriendRequestAcceptedIntegrationEvent(
            Ulid.NewUlid(), friendship.Id, friendship.RequesterId, friendship.AddresseeId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
