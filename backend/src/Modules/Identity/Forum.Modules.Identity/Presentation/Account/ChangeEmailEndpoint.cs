using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Account;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Account;

internal sealed class ChangeEmailEndpoint : IEndpoint
{
    private sealed record ChangeEmailRequest(string Email, string CurrentPassword);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/identity/me/email", static async (
                ChangeEmailRequest request,
                ICommandHandler<ChangeEmailCommand> handler,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                if (currentUser.Id is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var result = await handler.Handle(
                    new ChangeEmailCommand(userId, request.Email, request.CurrentPassword), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            // Each attempt spends an Argon2id verify — throttle like login so the current
            // password can't be brute-forced through this endpoint.
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("ChangeEmail")
            .WithTags("Identity");
}
