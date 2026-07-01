using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Forum.Common.Security;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Domain.Users;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Forum.Modules.Identity.Infrastructure.Security;

/// <summary>Issues short-lived HS256 access tokens carrying <c>sub</c>, username/email and global role claims.</summary>
internal sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    public AccessToken Issue(User user, IReadOnlyCollection<string> roles)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Ulid.NewUlid().ToString()),
            new("name", user.Username),
            new("email", user.Email),
        };
        claims.AddRange(roles.Select(static role => new Claim("role", role)));

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_options.SigningKeyBytes()), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);
        return new AccessToken(value, expires);
    }
}
