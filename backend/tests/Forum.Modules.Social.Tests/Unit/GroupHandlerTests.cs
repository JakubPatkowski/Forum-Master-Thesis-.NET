using Forum.Common.Security;
using Forum.Modules.Social.Application;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Groups;
using Forum.Modules.Social.Domain.Groups;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Social.Tests.Unit;

/// <summary>Group ownership invariants at the handler level: the owner can never leave or be kicked.</summary>
public sealed class GroupHandlerTests
{
    private readonly IGroupRepository _groups = Substitute.For<IGroupRepository>();
    private readonly IConversationRepository _conversations = Substitute.For<IConversationRepository>();
    private readonly IAclGrantService _aclGrants = Substitute.For<IAclGrantService>();
    private readonly INotificationRepository _notifications = Substitute.For<INotificationRepository>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private readonly Ulid _ownerId = Ulid.NewUlid();
    private readonly Group _group;

    public GroupHandlerTests()
    {
        _group = Group.Create("book-club", "demo", GroupVisibility.Public, _ownerId);
        _groups.GetByIdAsync(_group.Id, Arg.Any<CancellationToken>()).Returns(_group);
        _groups.GetMembershipAsync(_group.Id, _ownerId, Arg.Any<CancellationToken>())
            .Returns(new GroupMembership(_group.Id, _ownerId, DateTimeOffset.UtcNow, null));
    }

    private void ActAs(Ulid userId, bool moderator = false)
    {
        _currentUser.Id.Returns(userId);
        _currentUser.IsOwner(Arg.Any<Ulid>()).Returns(callInfo => callInfo.Arg<Ulid>() == userId);
        _currentUser.HasPermissionAsync(
                Permissions.Moderate, PermissionScopes.Group, Arg.Any<Ulid?>(), Arg.Any<CancellationToken>())
            .Returns(moderator);
    }

    [Fact]
    public async Task The_owner_cannot_leave_their_group()
    {
        ActAs(_ownerId);
        var handler = new LeaveGroupCommandHandler(
            _groups, _conversations, _aclGrants, _currentUser, _outbox, _unitOfWork, TimeProvider.System);

        var result = await handler.Handle(new LeaveGroupCommand(_group.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(GroupErrors.OwnerCannotLeave);
    }

    [Fact]
    public async Task The_owner_cannot_be_kicked_even_by_a_global_moderator()
    {
        var moderatorId = Ulid.NewUlid();
        ActAs(moderatorId, moderator: true);
        var handler = new KickGroupMemberCommandHandler(
            _groups, _conversations, _aclGrants, new Notifier(_notifications, _outbox), _currentUser,
            _outbox, _unitOfWork, TimeProvider.System);

        var result = await handler.Handle(new KickGroupMemberCommand(_group.Id, _ownerId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(GroupErrors.OwnerCannotBeKicked);
    }

    [Fact]
    public async Task A_plain_member_cannot_kick_anyone()
    {
        var memberId = Ulid.NewUlid();
        ActAs(memberId);
        var handler = new KickGroupMemberCommandHandler(
            _groups, _conversations, _aclGrants, new Notifier(_notifications, _outbox), _currentUser,
            _outbox, _unitOfWork, TimeProvider.System);

        var result = await handler.Handle(new KickGroupMemberCommand(_group.Id, _ownerId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SocialErrors.GroupForbidden);
    }

    [Fact]
    public async Task Ownership_transfers_only_to_a_current_member()
    {
        ActAs(_ownerId);
        var stranger = Ulid.NewUlid();
        _groups.GetMembershipAsync(_group.Id, stranger, Arg.Any<CancellationToken>())
            .Returns((GroupMembership?)null);
        var handler = new TransferGroupOwnershipCommandHandler(
            _groups, _currentUser, _outbox, _unitOfWork, TimeProvider.System);

        var result = await handler.Handle(
            new TransferGroupOwnershipCommand(_group.Id, stranger), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SocialErrors.TransferTargetNotMember);
        _group.OwnerId.ShouldBe(_ownerId);
    }

    [Fact]
    public async Task Promoting_a_member_grants_moderate_at_the_groups_acl_scope()
    {
        ActAs(_ownerId);
        var memberId = Ulid.NewUlid();
        _groups.GetMembershipAsync(_group.Id, memberId, Arg.Any<CancellationToken>())
            .Returns(new GroupMembership(_group.Id, memberId, DateTimeOffset.UtcNow, _ownerId));
        var handler = new SetGroupMemberRoleCommandHandler(
            _groups, _aclGrants, _currentUser, _outbox, _unitOfWork, TimeProvider.System);

        var promoted = await handler.Handle(
            new SetGroupMemberRoleCommand(_group.Id, memberId, "admin"), CancellationToken.None);
        promoted.IsSuccess.ShouldBeTrue();
        await _aclGrants.Received(1).GrantAsync(
            memberId, Permissions.Moderate, PermissionScopes.Group, _group.Id, Arg.Any<CancellationToken>());

        var demoted = await handler.Handle(
            new SetGroupMemberRoleCommand(_group.Id, memberId, "member"), CancellationToken.None);
        demoted.IsSuccess.ShouldBeTrue();
        await _aclGrants.Received(1).RevokeAsync(
            memberId, Permissions.Moderate, PermissionScopes.Group, _group.Id, Arg.Any<CancellationToken>());
    }
}
