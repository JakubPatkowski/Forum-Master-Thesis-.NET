namespace Forum.Modules.Identity.Application.Authentication;

/// <summary>
/// The result of a login/refresh: the access JWT (returned in the body, kept in JS memory) plus the opaque refresh
/// token (set by the endpoint as an httpOnly cookie, never exposed to JS).
/// </summary>
internal sealed record AuthTokensResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresOnUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresOnUtc);
