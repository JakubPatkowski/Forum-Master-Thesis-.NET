using Forum.Common.Security;
using Forum.Modules.Social.Application;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Friends;
using Forum.Modules.Social.Domain.Friendships;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Social.Tests.Unit;

/// <summary>The 404 → 403 → 422/409 ordering of the friend-request use case.</summary>
public sealed class SendFriendRequestHandlerTests
{
    private readonly IFriendshipRepository _friendships = Substitute.For<IFriendshipRepository>();
    private readonly IUserReader _users = Substitute.For<IUserReader>();
    private readonly ISocialBlockRepository _blocks = Substitute.For<ISocialBlockRepository>();
    private readonly IPrivacySettingsRepository _privacy = Substitute.For<IPrivacySettingsRepository>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly Ulid _addresseeId = Ulid.NewUlid();

    private SendFriendRequestCommandHandler CreateHandler()
    {
        _currentUser.Id.Returns(_userId);
        return new SendFriendRequestCommandHandler(
            _friendships, _users, new SocialInteractionGate(_blocks, _privacy, _friendships),
            new Notifier(_notifications, _outbox), _currentUser, _outbox, _unitOfWork, TimeProvider.System);
    }

    [Fact]
    public async Task An_unknown_or_banned_addressee_reads_not_found_before_any_other_gate()
    {
        _users.IsActiveAsync(_addresseeId, Arg.Any<CancellationToken>()).Returns(false);
        _blocks.AnyBetweenAsync(_userId, _addresseeId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new SendFriendRequestCommand(_addresseeId), CancellationToken.None);

        result.Error.ShouldBe(SocialErrors.UserNotFound); // 404 wins over the would-be 403.
    }

    [Fact]
    public async Task A_block_denies_with_the_generic_forbidden_error()
    {
        _users.IsActiveAsync(_addresseeId, Arg.Any<CancellationToken>()).Returns(true);
        _blocks.AnyBetweenAsync(_userId, _addresseeId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new SendFriendRequestCommand(_addresseeId), CancellationToken.None);

        result.Error.ShouldBe(SocialErrors.FriendRequestNotAllowed);
    }

    [Fact]
    public async Task An_existing_pair_row_conflicts_by_its_status()
    {
        _users.IsActiveAsync(_addresseeId, Arg.Any<CancellationToken>()).Returns(true);
        var pending = Friendship.Create(_addresseeId, _userId).Value;
        _friendships.GetBetweenAsync(_userId, _addresseeId, Arg.Any<CancellationToken>()).Returns(pending);

        var pendingResult = await CreateHandler().Handle(
            new SendFriendRequestCommand(_addresseeId), CancellationToken.None);
        pendingResult.Error.ShouldBe(SocialErrors.RequestAlreadyPending);

        pending.Accept(_userId, DateTimeOffset.UtcNow);
        var acceptedResult = await CreateHandler().Handle(
            new SendFriendRequestCommand(_addresseeId), CancellationToken.None);
        acceptedResult.Error.ShouldBe(SocialErrors.AlreadyFriends);
    }

    [Fact]
    public async Task A_successful_request_persists_notifies_the_addressee_and_publishes()
    {
        _users.IsActiveAsync(_addresseeId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateHandler().Handle(
            new SendFriendRequestCommand(_addresseeId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _friendships.Received(1).Add(Arg.Any<Friendship>());
        _notifications.Received(1).Add(Arg.Any<Domain.Notifications.Notification>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
