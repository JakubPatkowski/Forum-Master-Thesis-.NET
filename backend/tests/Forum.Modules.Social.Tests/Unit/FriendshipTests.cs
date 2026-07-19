using Forum.Modules.Social.Domain.Friendships;

using Shouldly;

using Xunit;

namespace Forum.Modules.Social.Tests.Unit;

/// <summary>Friendship state machine: pending → accepted; decline/cancel/unfriend are deletions, not states.</summary>
public sealed class FriendshipTests
{
    private readonly Ulid _requester = Ulid.NewUlid();
    private readonly Ulid _addressee = Ulid.NewUlid();

    [Fact]
    public void A_request_to_yourself_is_rejected()
    {
        var result = Friendship.Create(_requester, _requester);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FriendshipErrors.SelfRequest);
    }

    [Fact]
    public void A_new_request_starts_pending()
    {
        var friendship = Friendship.Create(_requester, _addressee).Value;

        friendship.Status.ShouldBe(FriendshipStatus.Pending);
        friendship.AcceptedOnUtc.ShouldBeNull();
    }

    [Fact]
    public void Only_the_addressee_may_accept()
    {
        var friendship = Friendship.Create(_requester, _addressee).Value;

        var byRequester = friendship.Accept(_requester, DateTimeOffset.UtcNow);
        byRequester.IsFailure.ShouldBeTrue();
        byRequester.Error.ShouldBe(FriendshipErrors.NotAddressee);

        var byAddressee = friendship.Accept(_addressee, DateTimeOffset.UtcNow);
        byAddressee.IsSuccess.ShouldBeTrue();
        friendship.Status.ShouldBe(FriendshipStatus.Accepted);
        friendship.AcceptedOnUtc.ShouldNotBeNull();
    }

    [Fact]
    public void An_accepted_friendship_cannot_be_accepted_again()
    {
        var friendship = Friendship.Create(_requester, _addressee).Value;
        friendship.Accept(_addressee, DateTimeOffset.UtcNow);

        var again = friendship.Accept(_addressee, DateTimeOffset.UtcNow);
        again.IsFailure.ShouldBeTrue();
        again.Error.ShouldBe(FriendshipErrors.NotPending);
    }

    [Fact]
    public void Involvement_and_the_other_side_resolve_from_either_end()
    {
        var friendship = Friendship.Create(_requester, _addressee).Value;

        friendship.Involves(_requester).ShouldBeTrue();
        friendship.Involves(_addressee).ShouldBeTrue();
        friendship.Involves(Ulid.NewUlid()).ShouldBeFalse();
        friendship.OtherThan(_requester).ShouldBe(_addressee);
        friendship.OtherThan(_addressee).ShouldBe(_requester);
    }
}
