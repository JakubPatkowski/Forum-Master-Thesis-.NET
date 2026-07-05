using System.Collections.Concurrent;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Forum.Common.Security;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Forum.Api.Realtime;

/// <summary>
/// Mints and redeems the short-lived, single-use connect tickets that authenticate the WebSocket handshake.
/// A browser WebSocket cannot send an Authorization header and the in-memory access token must not land in a
/// URL that proxies/history record, so the client trades its bearer token for a ticket via one REST call and
/// opens the socket with that instead. The ticket is a self-contained HS256 JWT (same signing key as access
/// tokens, but a dedicated audience so neither token kind can impersonate the other) — any replica can validate
/// it without shared state, consistent with ADR 0010's no-sticky-sessions scale-out. Single-use is enforced by
/// a per-replica redeemed-jti cache: a replay on this replica is rejected outright; replaying on another
/// replica is bounded by the seconds-long TTL (a shared cache is the Phase 10 hardening option if that residual
/// window ever matters).
/// </summary>
internal sealed class RealtimeTicketService
{
    private const string TicketAudienceSuffix = ":realtime";

    private readonly JwtOptions _jwt;
    private readonly RealtimeOptions _options;
    private readonly TimeProvider _clock;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly ConcurrentDictionary<string, DateTimeOffset> _redeemed = new();

    public RealtimeTicketService(IOptions<JwtOptions> jwt, IOptions<RealtimeOptions> options, TimeProvider clock)
    {
        _jwt = jwt.Value;
        _options = options.Value;
        _clock = clock;
    }

    public (string Ticket, int ExpiresInSeconds) Issue(Ulid userId)
    {
        var now = _clock.GetUtcNow();
        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience + TicketAudienceSuffix,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Ulid.NewUlid().ToString()),
            ],
            notBefore: now.UtcDateTime,
            expires: now.AddSeconds(_options.TicketTtlSeconds).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(_jwt.SigningKeyBytes()), SecurityAlgorithms.HmacSha256));

        return (_handler.WriteToken(token), _options.TicketTtlSeconds);
    }

    /// <summary>False for anything but a well-formed, unexpired, never-redeemed ticket minted by this cluster.</summary>
    public bool TryRedeem(string? ticket, out Ulid userId)
    {
        userId = default;
        if (string.IsNullOrWhiteSpace(ticket))
        {
            return false;
        }

        ClaimsPrincipal principal;
        SecurityToken validated;
        try
        {
            principal = _handler.ValidateToken(ticket, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _jwt.Issuer,
                ValidAudience = _jwt.Audience + TicketAudienceSuffix,
                IssuerSigningKey = new SymmetricSecurityKey(_jwt.SigningKeyBytes()),

                // Issuer and validator share a clock; skew would only widen the single-use replay window.
                ClockSkew = TimeSpan.Zero,
            }, out validated);
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            return false;
        }

        var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (jti is null || !Ulid.TryParse(sub, CultureInfo.InvariantCulture, out userId))
        {
            return false;
        }

        EvictExpired();

        // TryAdd is the atomic single-use gate: the second redeemer of the same jti loses.
        return _redeemed.TryAdd(jti, validated.ValidTo);
    }

    private void EvictExpired()
    {
        var now = _clock.GetUtcNow();
        foreach (var entry in _redeemed)
        {
            if (entry.Value < now)
            {
                _redeemed.TryRemove(entry.Key, out _);
            }
        }
    }
}
