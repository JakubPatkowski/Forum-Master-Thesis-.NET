using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Administration;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Administration;

internal sealed class SetUserStatusEndpoint : IEndpoint
{
    private sealed record SetStatusRequest(bool Block);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/identity/admin/users/{id}/status", static async (
                string id,
                SetStatusRequest request,
                ICommandHandler<SetUserStatusCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var userId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new SetUserStatusCommand(userId, request.Block), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequirePermission(Permissions.Manage)
            .WithName("SetUserStatus")
            .WithTags("Identity.Admin");
}
