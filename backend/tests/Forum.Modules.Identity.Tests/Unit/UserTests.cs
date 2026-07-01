using Forum.Modules.Identity.Domain.Users;
using Forum.Modules.Identity.Domain.Users.Events;

using Shouldly;

using Xunit;

namespace Forum.Modules.Identity.Tests.Unit;

public sealed class UserTests
{
    [Fact]
    public void Register_creates_an_active_user_and_raises_the_event()
    {
        var user = User.Register("JakubP", "Jakub@Example.com", "Jakub", "hash");

        user.Status.ShouldBe(UserStatus.Active);
        user.IsActive.ShouldBeTrue();
        user.UsernameLc.ShouldBe("jakubp");
        user.Username.ShouldBe("JakubP");

        var raised = user.DomainEvents.OfType<UserRegisteredDomainEvent>().ShouldHaveSingleItem();
        raised.UserId.ShouldBe(user.Id);
        raised.Email.ShouldBe("Jakub@Example.com");
    }

    [Fact]
    public void Block_marks_blocked_and_raises_the_event()
    {
        var user = User.Register("user", "user@example.com", "User", "hash");
        var moderator = Ulid.NewUlid();

        var result = user.Block(moderator);

        result.IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Blocked);
        user.IsActive.ShouldBeFalse();
        user.DomainEvents.OfType<UserBlockedDomainEvent>().ShouldHaveSingleItem().BlockedBy.ShouldBe(moderator);
    }

    [Fact]
    public void Blocking_an_already_blocked_user_fails()
    {
        var user = User.Register("user", "user@example.com", "User", "hash");
        user.Block(Ulid.NewUlid());

        var result = user.Block(Ulid.NewUlid());

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.AlreadyBlocked);
    }

    [Fact]
    public void Unblock_reactivates_a_blocked_user()
    {
        var user = User.Register("user", "user@example.com", "User", "hash");
        user.Block(Ulid.NewUlid());

        var result = user.Unblock();

        result.IsSuccess.ShouldBeTrue();
        user.Status.ShouldBe(UserStatus.Active);
    }

    [Fact]
    public void Unblocking_an_active_user_fails()
    {
        var user = User.Register("user", "user@example.com", "User", "hash");

        var result = user.Unblock();

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.NotBlocked);
    }
}
