using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Authentication;

internal sealed class LoginEndpoint : IEndpoint
{
    private sealed record LoginRequest(string Email, string Password);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/identity/login", static async (
                LoginRequest request,
                ICommandHandler<LoginCommand, AuthTokensResponse> handler,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var ip = http.Connection.RemoteIpAddress?.ToString();
                var userAgent = http.Request.Headers.UserAgent.ToString();

                var result = await handler.Handle(
                    new LoginCommand(request.Email, request.Password, ip, userAgent), cancellationToken);

                return result.Match(tokens =>
                {
                    RefreshTokenCookie.Set(http.Response, tokens.RefreshToken, tokens.RefreshTokenExpiresOnUtc);
                    return Results.Ok(new AccessTokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresOnUtc));
                });
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("Login")
            .WithTags("Identity");
}
