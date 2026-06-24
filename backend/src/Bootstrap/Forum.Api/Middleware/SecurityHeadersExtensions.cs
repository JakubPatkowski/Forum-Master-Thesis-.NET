namespace Forum.Api.Extensions;

public static class SecurityHeadersExtensions
{
    /// <summary>Baseline hardening headers (tighten CSP per frontend needs).</summary>
    public static IApplicationBuilder UseForumSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = "default-src 'self'";
            await next();
        });
}
