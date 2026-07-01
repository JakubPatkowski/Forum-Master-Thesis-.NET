using Forum.Modules.Identity.Domain.Tokens;

using Shouldly;

using Xunit;

namespace Forum.Modules.Identity.Tests.Unit;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IssueNew_starts_an_active_token_in_a_fresh_family()
    {
        var token = RefreshToken.IssueNew(Ulid.NewUlid(), "hash", Now.AddDays(14), Now, "127.0.0.1", "agent");

        token.Status.ShouldBe(RefreshTokenStatus.Active);
        token.IsActive(Now).ShouldBeTrue();
        token.FamilyId.ShouldNotBe(Ulid.Empty);
        token.RotatedTo.ShouldBeNull();
    }

    [Fact]
    public void Rotation_keeps_the_family_and_links_the_chain()
    {
        var original = RefreshToken.IssueNew(Ulid.NewUlid(), "hash-1", Now.AddDays(14), Now, null, null);

        var rotated = original.IssueNextInFamily("hash-2", Now.AddDays(14), Now, null, null);
        original.Rotate(rotated.Id);

        rotated.FamilyId.ShouldBe(original.FamilyId);
        rotated.Id.ShouldNotBe(original.Id);
        original.Status.ShouldBe(RefreshTokenStatus.Rotated);
        original.RotatedTo.ShouldBe(rotated.Id);
        original.IsActive(Now).ShouldBeFalse();
        rotated.IsActive(Now).ShouldBeTrue();
    }

    [Fact]
    public void Expired_token_is_not_active()
    {
        var token = RefreshToken.IssueNew(Ulid.NewUlid(), "hash", Now.AddDays(-1), Now.AddDays(-15), null, null);

        token.IsActive(Now).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_deactivates_the_token()
    {
        var token = RefreshToken.IssueNew(Ulid.NewUlid(), "hash", Now.AddDays(14), Now, null, null);

        token.Revoke();

        token.Status.ShouldBe(RefreshTokenStatus.Revoked);
        token.IsActive(Now).ShouldBeFalse();
    }
}
