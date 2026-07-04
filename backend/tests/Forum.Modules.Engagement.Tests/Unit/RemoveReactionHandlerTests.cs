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

public sealed class RemoveReactionHandlerTests
{
    private readonly IReactionTargetReader _targets = Substitute.For<IReactionTargetReader>();
    private readonly IReactionRepository _reactions = Substitute.For<IReactionRepository>();
    private readonly IEngagementQueries _queries = Substitute.For<IEngagementQueries>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly Ulid _userId = Ulid.NewUlid();
    private readonly Ulid _commentId = Ulid.NewUlid();
    private readonly Ulid _categoryId = Ulid.NewUlid();

    private RemoveReactionCommandHandler CreateHandler() => new(
        _targets, _reactions, _queries, _currentUser, _outbox, _unitOfWork, TimeProvider.System);

    private void SetUpVisibleTarget()
    {
        _currentUser.Id.Returns(_userId);
        _targets.GetAsync(ReactionTargetType.Comment, _commentId, Arg.Any<CancellationToken>())
            .Returns(new ReactionTarget(_categoryId, Ulid.NewUlid(), CategoryIsPrivate: false));
        _currentUser.HasPermissionAsync(
                Permissions.Like, PermissionScopes.Category, _categoryId, Arg.Any<CancellationToken>())
            .Returns(true);
        _queries.GetSummaryAsync(ReactionTargetType.Comment, _commentId, _userId, Arg.Any<CancellationToken>())
            .Returns(new ReactionSummaryResponse(_commentId, 0, false));
    }

    [Fact]
    public async Task Unliking_an_existing_like_removes_it_and_enqueues_the_event()
    {
        SetUpVisibleTarget();
        var reaction = new Reaction(
            _userId, ReactionTargetType.Comment, _commentId, ReactionTypes.Like, DateTimeOffset.UtcNow);
        _reactions.GetAsync(_userId, ReactionTargetType.Comment, _commentId, ReactionTypes.Like, Arg.Any<CancellationToken>())
            .Returns(reaction);

        var result = await CreateHandler().Handle(
            new RemoveReactionCommand(ReactionTargetType.Comment, _commentId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(0);
        result.Value.ViewerReacted.ShouldBeFalse();
        _reactions.Received(1).Remove(reaction);
        _outbox.Received(1).Enqueue(Arg.Is<ReactionRemovedIntegrationEvent>(evt =>
            evt.UserId == _userId && evt.TargetType == "comment" && evt.TargetId == _commentId));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unliking_something_never_liked_is_an_idempotent_no_op_that_still_succeeds()
    {
        SetUpVisibleTarget();

        var result = await CreateHandler().Handle(
            new RemoveReactionCommand(ReactionTargetType.Comment, _commentId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _reactions.DidNotReceive().Remove(Arg.Any<Reaction>());
        _outbox.DidNotReceive().Enqueue(Arg.Any<ReactionRemovedIntegrationEvent>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Anonymous_users_cannot_unlike()
    {
        _currentUser.Id.Returns((Ulid?)null);

        var result = await CreateHandler().Handle(
            new RemoveReactionCommand(ReactionTargetType.Comment, _commentId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.AuthenticationRequired);
    }

    [Fact]
    public async Task A_missing_target_is_not_found_for_unlike_too()
    {
        _currentUser.Id.Returns(_userId);
        _targets.GetAsync(ReactionTargetType.Comment, _commentId, Arg.Any<CancellationToken>())
            .Returns((ReactionTarget?)null);

        var result = await CreateHandler().Handle(
            new RemoveReactionCommand(ReactionTargetType.Comment, _commentId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.TargetNotFound);
    }
}
