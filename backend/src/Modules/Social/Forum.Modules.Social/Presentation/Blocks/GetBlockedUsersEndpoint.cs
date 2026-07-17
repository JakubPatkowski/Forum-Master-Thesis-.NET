using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Blocks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Blocks;

internal sealed class GetBlockedUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/blocks", static async (
                IQueryHandler<GetBlockedUsersQuery, IReadOnlyList<BlockedUserResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetBlockedUsersQuery(), cancellationToken);
                return result.Match(static blocks => Results.Ok(blocks));
            })
            .RequireAuthorization()
            .WithName("GetBlockedUsers")
            .WithTags("Social");
}
