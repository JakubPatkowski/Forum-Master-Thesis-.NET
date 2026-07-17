using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed class GetMyInvitesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/invites", static async (
                IQueryHandler<GetMyInvitesQuery, IReadOnlyList<GroupInviteResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetMyInvitesQuery(), cancellationToken);
                return result.Match(static invites => Results.Ok(invites));
            })
            .RequireAuthorization()
            .WithName("GetMyInvites")
            .WithTags("Social");
}
