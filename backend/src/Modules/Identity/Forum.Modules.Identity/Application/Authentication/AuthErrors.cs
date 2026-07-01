using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Authentication;

/// <summary>Authentication errors. Login/refresh collapse to a single generic error so account existence never leaks.</summary>
internal static class AuthErrors
{
    /// <summary>The one and only login failure — covers wrong email, wrong password and blocked account alike.</summary>
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password.");

    /// <summary>Generic refresh failure (unknown, expired, or reused token).</summary>
    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "Invalid or expired refresh token.");
}
