using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Authentication;

internal sealed class RegisterEndpoint : IEndpoint
{
    private sealed record RegisterRequest(string Username, string Email, string DisplayName, string Password);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/identity/register", static async (
                RegisterRequest request,
                ICommandHandler<RegisterUserCommand, RegisterUserResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(
                    new RegisterUserCommand(request.Username, request.Email, request.DisplayName, request.Password),
                    cancellationToken);

                return result.Match(response => Results.Created(
                    $"/api/identity/admin/users/{response.UserId}",
                    new { userId = response.UserId.ToString() }));
            })
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("RegisterUser")
            .WithTags("Identity");
}
