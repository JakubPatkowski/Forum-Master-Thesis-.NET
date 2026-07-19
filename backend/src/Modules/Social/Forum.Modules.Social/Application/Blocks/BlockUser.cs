using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Friendships;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Blocks;

/// <summary>
/// PUT-idempotent peer block. Creating one also severs the pair in the same transaction: the friendship row
/// (accepted → FriendRemoved, pending → FriendRequestDeclined, so the other side's views update without ever
/// learning why) and any pending group invites between the two. Existing conversations stay readable — only NEW
/// interactions are gated (send paths re-check the block live).
/// </summary>
internal sealed record BlockUserCommand(Ulid BlockedId) : ICommand;

internal sealed class BlockUserCommandHandler : ICommandHandler<BlockUserCommand>
{
    private readonly ISocialBlockRepository _blocks;
    private readonly IFriendshipRepository _friendships;
    private readonly IGroupInviteRepository _invites;
    private readonly IUserReader _users;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public BlockUserCommandHandler(
        ISocialBlockRepository blocks,
        IFriendshipRepository friendships,
        IGroupInviteRepository invites,
        IUserReader users,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _blocks = blocks;
        _friendships = friendships;
        _invites = invites;
        _users = users;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(BlockUserCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        if (!await _users.IsActiveAsync(command.BlockedId, cancellationToken))
        {
            return Result.Failure(SocialErrors.UserNotFound);
        }

        if (userId == command.BlockedId)
        {
            return Result.Failure(SocialErrors.SelfBlock);
        }

        if (await _blocks.GetAsync(userId, command.BlockedId, cancellationToken) is not null)
        {
            return Result.Success(); // Idempotent: already blocked.
        }

        var now = _clock.GetUtcNow();
        _blocks.Add(new SocialBlock(userId, command.BlockedId, now));

        var friendship = await _friendships.GetBetweenAsync(userId, command.BlockedId, cancellationToken);
        if (friendship is not null)
        {
            _friendships.Remove(friendship);
            if (friendship.Status == FriendshipStatus.Accepted)
            {
                _outbox.Enqueue(new FriendRemovedIntegrationEvent(
                    Ulid.NewUlid(), friendship.Id, friendship.RequesterId, friendship.AddresseeId, now));
            }
            else
            {
                _outbox.Enqueue(new FriendRequestDeclinedIntegrationEvent(
                    Ulid.NewUlid(), friendship.Id, friendship.RequesterId, friendship.AddresseeId, now));
            }
        }

        await _invites.RemoveBetweenAsync(userId, command.BlockedId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
