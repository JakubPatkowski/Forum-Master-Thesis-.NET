using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Friends;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Friends;

internal sealed class GetFriendsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/friends", static async (
                string? cursor,
                int? limit,
                IQueryHandler<GetFriendsQuery, CursorPage<FriendResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetFriendsQuery(cursor, limit), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .RequireAuthorization()
            .WithName("GetFriends")
            .WithTags("Social");
}
