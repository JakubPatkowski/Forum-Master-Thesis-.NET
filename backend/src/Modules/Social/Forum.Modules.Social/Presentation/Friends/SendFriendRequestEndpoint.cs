using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Friends;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Friends;

internal sealed record SendFriendRequestRequest(string AddresseeId);

internal sealed class SendFriendRequestEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/friends/requests", static async (
                SendFriendRequestRequest request,
                ICommandHandler<SendFriendRequestCommand, SendFriendRequestResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(request.AddresseeId, CultureInfo.InvariantCulture, out var addresseeId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new SendFriendRequestCommand(addresseeId), cancellationToken);
                return result.Match(static response =>
                    Results.Created($"/api/social/friends/requests/{response.FriendshipId}", response));
            })
            .RequireAuthorization()
            .WithName("SendFriendRequest")
            .WithTags("Social");
}
