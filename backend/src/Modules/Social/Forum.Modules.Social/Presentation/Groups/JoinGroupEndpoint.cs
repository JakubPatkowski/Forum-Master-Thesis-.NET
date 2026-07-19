using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed class JoinGroupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/groups/{groupId}/join", static async (
                string groupId,
                ICommandHandler<JoinGroupCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(groupId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new JoinGroupCommand(id), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("JoinGroup")
            .WithTags("Social");
}
