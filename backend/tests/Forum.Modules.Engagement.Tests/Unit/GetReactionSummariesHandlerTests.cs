using Forum.Common.Security;
using Forum.Modules.Engagement.Application;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Application.Reactions;
using Forum.Modules.Engagement.Domain.Reactions;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Engagement.Tests.Unit;

public sealed class GetReactionSummariesHandlerTests
{
    private readonly IEngagementQueries _queries = Substitute.For<IEngagementQueries>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();

    private GetReactionSummariesQueryHandler CreateHandler() => new(_queries, _currentUser);

    [Fact]
    public async Task An_unknown_target_type_is_rejected()
    {
        var result = await CreateHandler().Handle(
            new GetReactionSummariesQuery("category", [Ulid.NewUlid().ToString()]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.InvalidTargetType);
    }

    [Fact]
    public async Task An_empty_id_list_is_rejected()
    {
        var result = await CreateHandler().Handle(
            new GetReactionSummariesQuery("thread", []), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.NoTargets);
    }

    [Fact]
    public async Task An_oversized_id_list_is_rejected()
    {
        var ids = Enumerable.Range(0, EngagementErrors.MaxBatchTargets + 1)
            .Select(static _ => Ulid.NewUlid().ToString())
            .ToArray();

        var result = await CreateHandler().Handle(
            new GetReactionSummariesQuery("thread", ids), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.TooManyTargets);
    }

    [Fact]
    public async Task A_malformed_ulid_is_rejected()
    {
        var result = await CreateHandler().Handle(
            new GetReactionSummariesQuery("thread", ["not-a-ulid"]), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(EngagementErrors.InvalidTargetId);
    }

    [Fact]
    public async Task Duplicate_ids_collapse_and_the_batch_is_forwarded_once()
    {
        var id = Ulid.NewUlid();
        _queries.GetSummariesAsync(
                ReactionTargetType.Thread,
                Arg.Is<IReadOnlyList<Ulid>>(ids => ids.Count == 1 && ids[0] == id),
                Arg.Any<Ulid?>(),
                Arg.Any<CancellationToken>())
            .Returns([new ReactionSummaryResponse(id, 2, false)]);

        var result = await CreateHandler().Handle(
            new GetReactionSummariesQuery("thread", [id.ToString(), id.ToString()]), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var summary = result.Value.ShouldHaveSingleItem();
        summary.TargetId.ShouldBe(id);
        summary.Count.ShouldBe(2);
    }
}
