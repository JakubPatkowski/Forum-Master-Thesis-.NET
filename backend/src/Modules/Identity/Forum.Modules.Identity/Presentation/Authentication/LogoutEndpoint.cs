using Forum.Common.Cqrs;
using Forum.Common.Modules;
using Forum.Modules.Identity.Application.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Authentication;

internal sealed class LogoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/identity/logout", static async (
                ICommandHandler<LogoutCommand> handler,
                HttpContext http,
                CancellationToken cancellationToken) =>
            {
                await handler.Handle(new LogoutCommand(RefreshTokenCookie.Read(http.Request)), cancellationToken);
                RefreshTokenCookie.Clear(http.Response);
                return Results.NoContent();
            })
            .AllowAnonymous()
            .WithName("Logout")
            .WithTags("Identity");
}
