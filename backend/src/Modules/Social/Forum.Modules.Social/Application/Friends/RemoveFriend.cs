using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Friendships;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Friends;

/// <summary>Unfriends: deletes the ACCEPTED pair row (either side may). Pending pairs read as "not friends".</summary>
internal sealed record RemoveFriendCommand(Ulid OtherUserId) : ICommand;

internal sealed class RemoveFriendCommandHandler : ICommandHandler<RemoveFriendCommand>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public RemoveFriendCommandHandler(
        IFriendshipRepository friendships,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _friendships = friendships;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(RemoveFriendCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var friendship = await _friendships.GetBetweenAsync(userId, command.OtherUserId, cancellationToken);
        if (friendship is not { Status: FriendshipStatus.Accepted })
        {
            return Result.Failure(SocialErrors.NotFriends);
        }

        _friendships.Remove(friendship);
        _outbox.Enqueue(new FriendRemovedIntegrationEvent(
            Ulid.NewUlid(), friendship.Id, friendship.RequesterId, friendship.AddresseeId, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
