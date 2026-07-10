using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Account;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Account;

internal sealed class ChangePasswordEndpoint : IEndpoint
{
    private sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/identity/me/password", static async (
                ChangePasswordRequest request,
                ICommandHandler<ChangePasswordCommand> handler,
                ICurrentUser currentUser,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (currentUser.Id is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var result = await handler.Handle(
                    new ChangePasswordCommand(userId, request.CurrentPassword, request.NewPassword),
                    cancellationToken);
                return result.Match(() =>
                {
                    // Every refresh token is revoked — clear the caller's cookie too, same as logout-all.
                    RefreshTokenCookie.Clear(http.Response);
                    return Results.NoContent();
                });
            })
            .RequireAuthorization()
            // Each attempt spends an Argon2id verify — throttle like login so the current
            // password can't be brute-forced through this endpoint.
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ChangePassword")
            .WithTags("Identity");
}
