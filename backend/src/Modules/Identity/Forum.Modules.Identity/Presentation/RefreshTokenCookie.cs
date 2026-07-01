using Microsoft.AspNetCore.Http;

namespace Forum.Modules.Identity.Presentation;

/// <summary>The httpOnly refresh-token cookie: never readable by JS, scoped to the identity endpoints that rotate it.</summary>
internal static class RefreshTokenCookie
{
    public const string Name = "refresh_token";
    private const string Path = "/api/identity";

    public static void Set(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, BuildOptions(response, expires));

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, BuildOptions(response, expires: null));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) ? value : null;

    private static CookieOptions BuildOptions(HttpResponse response, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        // Secure follows the transport so local http dev works while TLS deployments stay secure.
        Secure = response.HttpContext.Request.IsHttps,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expires,
        IsEssential = true,
    };
}
