using Forum.Modules.Identity.Infrastructure.Security;

using Shouldly;

using Xunit;

namespace Forum.Modules.Identity.Tests.Unit;

public sealed class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_round_trips_the_correct_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        hash.ShouldStartWith("$argon2id$");
        _hasher.Verify(hash, "correct horse battery staple").ShouldBeTrue();
    }

    [Fact]
    public void Verify_rejects_a_wrong_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        _hasher.Verify(hash, "Tr0ub4dor&3").ShouldBeFalse();
    }

    [Fact]
    public void Hash_produces_distinct_outputs_for_the_same_password()
    {
        // Random per-hash salt => different encoded strings, both verifiable.
        var first = _hasher.Hash("same-password");
        var second = _hasher.Hash("same-password");

        first.ShouldNotBe(second);
        _hasher.Verify(first, "same-password").ShouldBeTrue();
        _hasher.Verify(second, "same-password").ShouldBeTrue();
    }

    [Fact]
    public void VerifyDummy_does_not_throw_for_the_not_found_path()
    {
        Should.NotThrow(() => _hasher.VerifyDummy("any-password"));
    }
}
