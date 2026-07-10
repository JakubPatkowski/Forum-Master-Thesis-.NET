using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Account;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Account;

internal sealed class ChangeUsernameEndpoint : IEndpoint
{
    private sealed record ChangeUsernameRequest(string Username);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/identity/me/username", static async (
                ChangeUsernameRequest request,
                ICommandHandler<ChangeUsernameCommand> handler,
                ICurrentUser currentUser,
                CancellationToken cancellationToken) =>
            {
                if (currentUser.Id is not { } userId)
                {
                    return Results.Unauthorized();
                }

                var result = await handler.Handle(
                    new ChangeUsernameCommand(userId, request.Username), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("ChangeUsername")
            .WithTags("Identity");
}
