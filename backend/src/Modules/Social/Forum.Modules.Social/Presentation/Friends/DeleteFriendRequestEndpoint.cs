using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Friends;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Friends;

/// <summary>DELETE covers decline (addressee) and cancel (requester) — the same pending-row deletion.</summary>
internal sealed class DeleteFriendRequestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/social/friends/requests/{friendshipId}", static async (
                string friendshipId,
                ICommandHandler<DeleteFriendRequestCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(friendshipId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new DeleteFriendRequestCommand(id), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("DeleteFriendRequest")
            .WithTags("Social");
}
