using Forum.Modules.Identity.Domain.Users;

namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>A signed access token plus its absolute expiry.</summary>
internal sealed record AccessToken(string Value, DateTimeOffset ExpiresOnUtc);

/// <summary>Issues short-lived access JWTs carrying the user's identity and global roles.</summary>
internal interface IJwtTokenService
{
    AccessToken Issue(User user, IReadOnlyCollection<string> roles);
}
