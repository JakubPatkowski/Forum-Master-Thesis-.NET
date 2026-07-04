using Forum.Modules.Engagement.Application;
using Forum.Modules.Engagement.Domain.Reactions;

using Shouldly;

using Xunit;

namespace Forum.Modules.Engagement.Tests.Unit;

public sealed class ReactionTargetsTests
{
    [Fact]
    public void Known_wire_names_parse_case_insensitively()
    {
        ReactionTargets.TryParse("thread", out var thread).ShouldBeTrue();
        thread.ShouldBe(ReactionTargetType.Thread);

        ReactionTargets.TryParse("comment", out var comment).ShouldBeTrue();
        comment.ShouldBe(ReactionTargetType.Comment);

        ReactionTargets.TryParse(" Thread ", out var trimmed).ShouldBeTrue();
        trimmed.ShouldBe(ReactionTargetType.Thread);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("category")]
    [InlineData("user")]
    public void Unknown_wire_names_are_rejected(string? wire) =>
        ReactionTargets.TryParse(wire, out _).ShouldBeFalse();

    [Fact]
    public void Wire_names_round_trip()
    {
        foreach (var target in new[] { ReactionTargetType.Thread, ReactionTargetType.Comment })
        {
            ReactionTargets.TryParse(ReactionTargets.ToWire(target), out var parsed).ShouldBeTrue();
            parsed.ShouldBe(target);
        }
    }
}
