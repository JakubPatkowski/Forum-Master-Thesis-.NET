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

internal sealed class AddAclEntryEndpoint : IEndpoint
{
    private sealed record AclEntryRequest(string Scope, string? ScopeId, int AllowBits, int DenyBits);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/identity/admin/users/{id}/acl", static async (
                string id,
                AclEntryRequest request,
                ICommandHandler<AddAclEntryCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var userId))
                {
                    return Results.NotFound();
                }

                Ulid? scopeId = null;
                if (!string.IsNullOrWhiteSpace(request.ScopeId))
                {
                    if (!Ulid.TryParse(request.ScopeId, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return Results.BadRequest(new { error = "Invalid scope id." });
                    }

                    scopeId = parsed;
                }

                var result = await handler.Handle(
                    new AddAclEntryCommand(userId, request.Scope, scopeId, request.AllowBits, request.DenyBits),
                    cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequirePermission(Permissions.Manage)
            .WithName("AddAclEntry")
            .WithTags("Identity.Admin");
}
