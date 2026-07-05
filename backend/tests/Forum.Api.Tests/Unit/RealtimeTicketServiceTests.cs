using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Forum.Api.Realtime;
using Forum.Common.Security;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>
/// The connect-ticket lifecycle: a fresh ticket redeems exactly once for the right user; expired, forged,
/// malformed tickets and plain access tokens (wrong audience) never authenticate a socket.
/// </summary>
public sealed class RealtimeTicketServiceTests
{
    private static readonly JwtOptions Jwt = new()
    {
        SigningKey = "realtime-ticket-tests-signing-key-0123456789-abcdefghijklmnop",
    };

    private readonly Ulid _userId = Ulid.NewUlid();

    private static RealtimeTicketService CreateService(TimeProvider? clock = null) => new(
        Options.Create(Jwt), Options.Create(new RealtimeOptions()), clock ?? TimeProvider.System);

    [Fact]
    public void A_fresh_ticket_redeems_once_for_the_issuing_user_and_never_twice()
    {
        var service = CreateService();
        var (ticket, expiresInSeconds) = service.Issue(_userId);
        expiresInSeconds.ShouldBe(30);

        service.TryRedeem(ticket, out var redeemedUser).ShouldBeTrue();
        redeemedUser.ShouldBe(_userId);

        // Single-use: the same ticket presented again (log scrape, history replay) is dead.
        service.TryRedeem(ticket, out _).ShouldBeFalse();
    }

    [Fact]
    public void An_expired_ticket_is_rejected()
    {
        // Issued 40 s in the past (per the service's clock) → already past its 30 s TTL for the validator.
        var issuedInThePast = CreateService(new ShiftedClock(TimeSpan.FromSeconds(-40)));
        var (ticket, _) = issuedInThePast.Issue(_userId);

        CreateService().TryRedeem(ticket, out _).ShouldBeFalse();
    }

    [Fact]
    public void Garbage_and_missing_tickets_are_rejected()
    {
        var service = CreateService();
        service.TryRedeem(null, out _).ShouldBeFalse();
        service.TryRedeem("", out _).ShouldBeFalse();
        service.TryRedeem("not-a-jwt", out _).ShouldBeFalse();
    }

    [Fact]
    public void An_access_token_cannot_be_used_as_a_ticket()
    {
        // Same key and issuer, but the ACCESS audience — exactly what a stolen bearer token would look like.
        var accessLookalike = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: Jwt.Issuer,
            audience: Jwt.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, _userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Ulid.NewUlid().ToString()),
            ],
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Jwt.SigningKeyBytes()), SecurityAlgorithms.HmacSha256)));

        CreateService().TryRedeem(accessLookalike, out _).ShouldBeFalse();
    }

    /// <summary>A clock offset from the real one — lets a test issue tickets "in the past".</summary>
    private sealed class ShiftedClock(TimeSpan shift) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + shift;
    }
}
