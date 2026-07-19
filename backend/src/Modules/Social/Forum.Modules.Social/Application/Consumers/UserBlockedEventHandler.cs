using Forum.Common.Messaging;
using Forum.Modules.Identity.Contracts.IntegrationEvents;
using Forum.Modules.Social.Application.Abstractions;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Social.Application.Consumers;

/// <summary>
/// Reacts to Identity's ADMIN moderation ban (NOT a peer <c>SocialBlock</c> — different concept, deliberately
/// different name): a suspended account's pending friend requests and group invites vanish in both directions, so
/// nobody is left with an actionable invitation from a banned user. Existing friendships/memberships stay — the
/// account itself is disabled at the door, and an unban restores its social graph untouched.
/// </summary>
internal sealed class UserBlockedEventHandler : IIntegrationEventHandler<UserBlockedIntegrationEvent>
{
    private readonly IFriendshipRepository _friendships;
    private readonly IGroupInviteRepository _invites;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<UserBlockedEventHandler> _logger;

    public UserBlockedEventHandler(
        IFriendshipRepository friendships,
        IGroupInviteRepository invites,
        IUnitOfWork unitOfWork,
        ILogger<UserBlockedEventHandler> logger)
    {
        _friendships = friendships;
        _invites = invites;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(UserBlockedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        await _friendships.RemovePendingInvolvingAsync(integrationEvent.UserId, cancellationToken);
        await _invites.RemoveInvolvingAsync(integrationEvent.UserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Cleared pending friend requests and group invites for banned user {UserId}.",
                integrationEvent.UserId);
        }
    }
}
