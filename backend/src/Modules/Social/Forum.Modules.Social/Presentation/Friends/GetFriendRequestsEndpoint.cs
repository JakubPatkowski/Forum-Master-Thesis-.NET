using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Friends;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Friends;

internal sealed class GetFriendRequestsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/friends/requests", static async (
                IQueryHandler<GetFriendRequestsQuery, FriendRequestsResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetFriendRequestsQuery(), cancellationToken);
                return result.Match(static requests => Results.Ok(requests));
            })
            .RequireAuthorization()
            .WithName("GetFriendRequests")
            .WithTags("Social");
}
