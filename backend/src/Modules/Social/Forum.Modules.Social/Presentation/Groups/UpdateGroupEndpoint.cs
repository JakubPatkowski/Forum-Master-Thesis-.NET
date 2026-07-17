using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed record UpdateGroupRequest(string Name, string? Description, string? Visibility);

internal sealed class UpdateGroupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/social/groups/{groupId}", static async (
                string groupId,
                UpdateGroupRequest request,
                ICommandHandler<UpdateGroupCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(groupId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var command = new UpdateGroupCommand(
                    id, request.Name ?? string.Empty, request.Description ?? string.Empty,
                    request.Visibility ?? "public");
                var result = await handler.Handle(command, cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("UpdateGroup")
            .WithTags("Social");
}
