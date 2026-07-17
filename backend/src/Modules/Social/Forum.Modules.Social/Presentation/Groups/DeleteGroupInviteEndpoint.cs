using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

/// <summary>DELETE covers decline (invitee) and cancel (inviter) — the same pending-row deletion.</summary>
internal sealed class DeleteGroupInviteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/social/invites/{inviteId}", static async (
                string inviteId,
                ICommandHandler<DeleteGroupInviteCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(inviteId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new DeleteGroupInviteCommand(id), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("DeleteGroupInvite")
            .WithTags("Social");
}
