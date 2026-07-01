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

internal sealed class AssignRolesEndpoint : IEndpoint
{
    private sealed record AssignRoleRequest(string Role, bool Assign);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/identity/admin/users/{id}/roles", static async (
                string id,
                AssignRoleRequest request,
                ICommandHandler<AssignRoleCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var userId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new AssignRoleCommand(userId, request.Role, request.Assign), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequirePermission(Permissions.Manage)
            .WithName("AssignRole")
            .WithTags("Identity.Admin");
}
