using Forum.Common.Cqrs;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Authentication;

internal sealed class LogoutAllEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/identity/logout-all", static async (
                ICommandHandler<LogoutAllCommand> handler,
                ICurrentUser currentUser,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                if (currentUser.Id is not { } userId)
                {
                    return Results.Unauthorized();
                }

                await handler.Handle(new LogoutAllCommand(userId), cancellationToken);
                RefreshTokenCookie.Clear(http.Response);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .WithName("LogoutAll")
            .WithTags("Identity");
}
