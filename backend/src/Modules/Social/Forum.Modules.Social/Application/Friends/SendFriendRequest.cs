using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Friendships;
using Forum.Modules.Social.Domain.Notifications;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Friends;

/// <summary>
/// Sends a friend request. Order: addressee exists 404 → block/privacy 403 (one generic error — a block is
/// indistinguishable from a privacy setting on the wire) → self 422 → pair conflicts 409. The addressee gets a
/// durable notification + realtime ping; the requester's own views update off the integration event.
/// </summary>
internal sealed record SendFriendRequestCommand(Ulid AddresseeId) : ICommand<SendFriendRequestResponse>;

internal sealed record SendFriendRequestResponse(Ulid FriendshipId);

internal sealed class SendFriendRequestCommandHandler
    : ICommandHandler<SendFriendRequestCommand, SendFriendRequestResponse>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IUserReader _users;
    private readonly SocialInteractionGate _gate;
    private readonly Notifier _notifier;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public SendFriendRequestCommandHandler(
        IFriendshipRepository friendships,
        IUserReader users,
        SocialInteractionGate gate,
        Notifier notifier,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _friendships = friendships;
        _users = users;
        _gate = gate;
        _notifier = notifier;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<SendFriendRequestResponse>> Handle(
        SendFriendRequestCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<SendFriendRequestResponse>(SocialErrors.AuthenticationRequired);
        }

        if (!await _users.IsActiveAsync(command.AddresseeId, cancellationToken))
        {
            return Result.Failure<SendFriendRequestResponse>(SocialErrors.UserNotFound);
        }

        if (userId != command.AddresseeId)
        {
            var allowed = await _gate.MayFriendRequestAsync(userId, command.AddresseeId, cancellationToken);
            if (allowed.IsFailure)
            {
                return Result.Failure<SendFriendRequestResponse>(allowed.Error);
            }
        }

        var existing = await _friendships.GetBetweenAsync(userId, command.AddresseeId, cancellationToken);
        if (existing is not null)
        {
            return Result.Failure<SendFriendRequestResponse>(existing.Status == FriendshipStatus.Accepted
                ? SocialErrors.AlreadyFriends
                : SocialErrors.RequestAlreadyPending);
        }

        var created = Friendship.Create(userId, command.AddresseeId);
        if (created.IsFailure)
        {
            return Result.Failure<SendFriendRequestResponse>(created.Error);
        }

        var friendship = created.Value;
        _friendships.Add(friendship);

        var now = _clock.GetUtcNow();
        _notifier.Notify(command.AddresseeId, NotificationKinds.FriendRequest, userId, friendship.Id, now);
        _outbox.Enqueue(new FriendRequestSentIntegrationEvent(
            Ulid.NewUlid(), friendship.Id, userId, command.AddresseeId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new SendFriendRequestResponse(friendship.Id));
    }
}
