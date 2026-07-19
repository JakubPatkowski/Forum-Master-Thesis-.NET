using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed class AcceptGroupInviteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/invites/{inviteId}/accept", static async (
                string inviteId,
                ICommandHandler<AcceptGroupInviteCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(inviteId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new AcceptGroupInviteCommand(id), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("AcceptGroupInvite")
            .WithTags("Social");
}
