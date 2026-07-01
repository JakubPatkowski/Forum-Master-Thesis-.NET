namespace Forum.Modules.Identity.Presentation;

/// <summary>The access token returned to the client (kept in JS memory); the refresh token rides the httpOnly cookie.</summary>
internal sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresOnUtc);
