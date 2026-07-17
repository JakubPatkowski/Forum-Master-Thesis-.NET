using Forum.Modules.Social.Application;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Friendships;
using Forum.Modules.Social.Domain.Privacy;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Social.Tests.Unit;

/// <summary>
/// The one shared block + privacy gate: a block (either direction) and a privacy setting must be
/// indistinguishable on the wire (same generic Forbidden error per interaction kind).
/// </summary>
public sealed class SocialInteractionGateTests
{
    private readonly ISocialBlockRepository _blocks = Substitute.For<ISocialBlockRepository>();
    private readonly IPrivacySettingsRepository _privacy = Substitute.For<IPrivacySettingsRepository>();
    private readonly IFriendshipRepository _friendships = Substitute.For<IFriendshipRepository>();

    private readonly Ulid _actor = Ulid.NewUlid();
    private readonly Ulid _target = Ulid.NewUlid();

    private SocialInteractionGate CreateGate() => new(_blocks, _privacy, _friendships);

    private void SetTargetAudience(PrivacyAudience audience)
    {
        var settings = new UserPrivacySettings(_target);
        settings.Update(audience, audience, audience, showOnlineStatus: true);
        _privacy.GetAsync(_target, Arg.Any<CancellationToken>()).Returns(settings);
    }

    [Fact]
    public async Task No_settings_row_means_everyone_may_interact()
    {
        _privacy.GetAsync(_target, Arg.Any<CancellationToken>()).Returns((UserPrivacySettings?)null);

        (await CreateGate().MayMessageAsync(_actor, _target, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await CreateGate().MayFriendRequestAsync(_actor, _target, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await CreateGate().MayInviteToGroupAsync(_actor, _target, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_block_in_either_direction_denies_with_the_same_generic_error_as_privacy()
    {
        _blocks.AnyBetweenAsync(_actor, _target, Arg.Any<CancellationToken>()).Returns(true);

        var blocked = await CreateGate().MayMessageAsync(_actor, _target, CancellationToken.None);
        blocked.Error.ShouldBe(SocialErrors.MessageNotAllowed);

        _blocks.AnyBetweenAsync(_actor, _target, Arg.Any<CancellationToken>()).Returns(false);
        SetTargetAudience(PrivacyAudience.NoOne);

        var refused = await CreateGate().MayMessageAsync(_actor, _target, CancellationToken.None);
        refused.Error.ShouldBe(SocialErrors.MessageNotAllowed); // Identical — a block never reveals itself.
    }

    [Fact]
    public async Task Friends_only_messaging_requires_an_accepted_friendship()
    {
        SetTargetAudience(PrivacyAudience.Friends);
        _friendships.GetBetweenAsync(_actor, _target, Arg.Any<CancellationToken>())
            .Returns((Friendship?)null);

        var strangers = await CreateGate().MayMessageAsync(_actor, _target, CancellationToken.None);
        strangers.Error.ShouldBe(SocialErrors.MessageNotAllowed);

        var pending = Friendship.Create(_actor, _target).Value;
        _friendships.GetBetweenAsync(_actor, _target, Arg.Any<CancellationToken>()).Returns(pending);
        (await CreateGate().MayMessageAsync(_actor, _target, CancellationToken.None)).IsFailure.ShouldBeTrue();

        pending.Accept(_target, DateTimeOffset.UtcNow);
        (await CreateGate().MayMessageAsync(_actor, _target, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Each_interaction_kind_reads_its_own_audience_setting()
    {
        var settings = new UserPrivacySettings(_target);
        settings.Update(
            PrivacyAudience.NoOne, PrivacyAudience.Everyone, PrivacyAudience.NoOne, showOnlineStatus: true);
        _privacy.GetAsync(_target, Arg.Any<CancellationToken>()).Returns(settings);

        (await CreateGate().MayFriendRequestAsync(_actor, _target, CancellationToken.None))
            .Error.ShouldBe(SocialErrors.FriendRequestNotAllowed);
        (await CreateGate().MayMessageAsync(_actor, _target, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await CreateGate().MayInviteToGroupAsync(_actor, _target, CancellationToken.None))
            .Error.ShouldBe(SocialErrors.GroupInviteNotAllowed);
    }
}
