using Forum.Modules.Social.Domain.Privacy;

using Shouldly;

using Xunit;

namespace Forum.Modules.Social.Tests.Unit;

public sealed class UserPrivacySettingsTests
{
    [Fact]
    public void A_fresh_row_defaults_to_everyone_and_visible()
    {
        var settings = new UserPrivacySettings(Ulid.NewUlid());

        settings.FriendRequests.ShouldBe(PrivacyAudience.Everyone);
        settings.Messages.ShouldBe(PrivacyAudience.Everyone);
        settings.GroupInvites.ShouldBe(PrivacyAudience.Everyone);
        settings.ShowOnlineStatus.ShouldBeTrue();
    }

    [Fact]
    public void Friends_only_friend_requests_normalize_to_no_one()
    {
        var settings = new UserPrivacySettings(Ulid.NewUlid());

        // "Friends may send friend requests" is meaningless — they already are friends.
        settings.Update(
            PrivacyAudience.Friends, PrivacyAudience.Friends, PrivacyAudience.NoOne, showOnlineStatus: false);

        settings.FriendRequests.ShouldBe(PrivacyAudience.NoOne);
        settings.Messages.ShouldBe(PrivacyAudience.Friends);
        settings.GroupInvites.ShouldBe(PrivacyAudience.NoOne);
        settings.ShowOnlineStatus.ShouldBeFalse();
    }
}
