using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Friends;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Friends;

internal sealed class AcceptFriendRequestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/friends/requests/{friendshipId}/accept", static async (
                string friendshipId,
                ICommandHandler<AcceptFriendRequestCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(friendshipId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new AcceptFriendRequestCommand(id), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("AcceptFriendRequest")
            .WithTags("Social");
}
