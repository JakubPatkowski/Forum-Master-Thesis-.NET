using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Identity.Application.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Authentication;

internal sealed class RefreshEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/identity/refresh", static async (
                ICommandHandler<RefreshTokenCommand, AuthTokensResponse> handler,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                var token = RefreshTokenCookie.Read(http.Request) ?? string.Empty;
                var ip = http.Connection.RemoteIpAddress?.ToString();
                var userAgent = http.Request.Headers.UserAgent.ToString();

                var result = await handler.Handle(new RefreshTokenCommand(token, ip, userAgent), cancellationToken);
                if (result.IsFailure)
                {
                    // A bad/stale/reused token is no longer usable — drop the cookie.
                    RefreshTokenCookie.Clear(http.Response);
                    return ApiResults.Problem(result.Error);
                }

                var tokens = result.Value;
                RefreshTokenCookie.Set(http.Response, tokens.RefreshToken, tokens.RefreshTokenExpiresOnUtc);
                return Results.Ok(new AccessTokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresOnUtc));
            })
            .AllowAnonymous()
            .WithName("RefreshToken")
            .WithTags("Identity");
}
