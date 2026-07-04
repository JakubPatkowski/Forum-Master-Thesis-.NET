using Forum.Common.Security;
using Forum.Modules.Engagement.Application;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Application.Reactions;
using Forum.Modules.Engagement.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Domain.Reactions;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Engagement.Tests.Unit;

public sealed class AddReactionHandlerTests
{
    private readonly IReactionTargetReader _targets = Substitute.For<IReactionTargetReader>();
    private readonly IReactionRepository _reactions = Substitute.For<IReactionRepository>();
    private readonly IEngagementQueries _queries = Substitute.For<IEngagementQueries>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly Ulid _threadId = Ulid.NewUlid();
    private readonly Ulid _categoryId = Ulid.NewUlid();

    private AddReactionCommandHandler CreateHandler() => new(
        _targets, _reactions, _queries, _currentUser, _outbox, _unitOfWork, TimeProvider.System);

    private void SetUpVisibleTarget(bool isPrivate = false, Ulid? categoryOwnerId = null)
    {
        _currentUser.Id.Returns(_userId);
        _currentUser.IsOwner(Arg.Any<Ulid>()).Returns(callInfo => callInfo.Arg<Ulid>() == _userId);
        _targets.GetAsync(ReactionTargetType.Thread, _threadId, Arg.Any<CancellationToken>())
            .Returns(new ReactionTarget(_categoryId, categoryOwnerId ?? Ulid.NewUlid(), isPrivate));
        _currentUser.HasPermissionAsync(
                Permissions.Like, PermissionScopes.Category, _categoryId, Arg.Any<CancellationToken>())
            .Returns(true);
        _queries.GetSummaryAsync(ReactionTargetType.Thread, _threadId, _userId, Arg.Any<CancellationToken>())
            .Returns(new ReactionSummaryResponse(_threadId, 1, true));
    }

    [Fact]
    public async Task Anonymous_users_cannot_react()
    {
        _currentUser.Id.Returns((Ulid?)null);

        var result = await CreateHandler().Handle(
            new AddReactionCommand(ReactionTargetType.Thread, _threadId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.AuthenticationRequired);
    }

    [Fact]
    public async Task A_missing_or_deleted_target_is_not_found()
    {
        _currentUser.Id.Returns(_userId);
        _targets.GetAsync(ReactionTargetType.Thread, _threadId, Arg.Any<CancellationToken>())
            .Returns((ReactionTarget?)null);

        var result = await CreateHandler().Handle(
            new AddReactionCommand(ReactionTargetType.Thread, _threadId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.TargetNotFound);
    }

    [Fact]
    public async Task A_private_category_rejects_outsiders_before_the_permission_check()
    {
        SetUpVisibleTarget(isPrivate: true);

        var result = await CreateHandler().Handle(
            new AddReactionCommand(ReactionTargetType.Thread, _threadId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.PrivateCategory);
    }

    [Fact]
    public async Task The_category_owner_may_react_in_their_private_category()
    {
        SetUpVisibleTarget(isPrivate: true, categoryOwnerId: _userId);

        var result = await CreateHandler().Handle(
            new AddReactionCommand(ReactionTargetType.Thread, _threadId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _reactions.Received(1).Add(Arg.Any<Reaction>());
    }

    [Fact]
    public async Task Without_the_like_permission_the_toggle_is_forbidden()
    {
        SetUpVisibleTarget();
        _currentUser.HasPermissionAsync(
                Permissions.Like, PermissionScopes.Category, _categoryId, Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await CreateHandler().Handle(
            new AddReactionCommand(ReactionTargetType.Thread, _threadId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.LikeForbidden);
        _reactions.DidNotReceive().Add(Arg.Any<Reaction>());
    }

    [Fact]
    public async Task The_first_like_stores_a_reaction_and_enqueues_the_event()
    {
        SetUpVisibleTarget();

        var result = await CreateHandler().Handle(
            new AddReactionCommand(ReactionTargetType.Thread, _threadId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1);
        result.Value.ViewerReacted.ShouldBeTrue();
        _reactions.Received(1).Add(Arg.Is<Reaction>(reaction =>
            reaction.UserId == _userId
            && reaction.TargetType == ReactionTargetType.Thread
            && reaction.TargetId == _threadId
            && reaction.ReactionType == ReactionTypes.Like
            && reaction.Value == 1));
        _outbox.Received(1).Enqueue(Arg.Is<ReactionAddedIntegrationEvent>(evt =>
            evt.UserId == _userId && evt.TargetType == "thread" && evt.TargetId == _threadId
            && evt.ReactionType == ReactionTypes.Like));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Re_liking_is_an_idempotent_no_op_that_still_succeeds()
    {
        SetUpVisibleTarget();
        _reactions.GetAsync(_userId, ReactionTargetType.Thread, _threadId, ReactionTypes.Like, Arg.Any<CancellationToken>())
            .Returns(new Reaction(_userId, ReactionTargetType.Thread, _threadId, ReactionTypes.Like, DateTimeOffset.UtcNow));

        var result = await CreateHandler().Handle(
            new AddReactionCommand(ReactionTargetType.Thread, _threadId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(1); // unchanged
        _reactions.DidNotReceive().Add(Arg.Any<Reaction>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<ReactionAddedIntegrationEvent>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
