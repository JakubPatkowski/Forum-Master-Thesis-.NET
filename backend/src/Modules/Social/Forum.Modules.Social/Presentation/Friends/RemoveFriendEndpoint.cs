using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Friends;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Friends;

internal sealed class RemoveFriendEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/social/friends/{userId}", static async (
                string userId,
                ICommandHandler<RemoveFriendCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(userId, CultureInfo.InvariantCulture, out var otherUserId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new RemoveFriendCommand(otherUserId), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("RemoveFriend")
            .WithTags("Social");
}
