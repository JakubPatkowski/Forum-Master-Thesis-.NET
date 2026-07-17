using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Friendships;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Friends;

/// <summary>
/// Removes a PENDING request — decline (addressee) and cancel (requester) are the same deletion, so one endpoint
/// covers both. Accepted friendships are removed via the unfriend use case instead (422 here). Deletion is
/// silent for the other party's bell (no notification row — declining quietly is the humane default); their open
/// views still update off the integration event.
/// </summary>
internal sealed record DeleteFriendRequestCommand(Ulid FriendshipId) : ICommand;

internal sealed class DeleteFriendRequestCommandHandler : ICommandHandler<DeleteFriendRequestCommand>
{
    private readonly IFriendshipRepository _friendships;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public DeleteFriendRequestCommandHandler(
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

    public async Task<Result> Handle(DeleteFriendRequestCommand command, CancellationToken cancellationToken)
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

        if (friendship.Status != FriendshipStatus.Pending)
        {
            return Result.Failure(SocialErrors.NotAFriendRequest);
        }

        _friendships.Remove(friendship);
        _outbox.Enqueue(new FriendRequestDeclinedIntegrationEvent(
            Ulid.NewUlid(), friendship.Id, friendship.RequesterId, friendship.AddresseeId, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
