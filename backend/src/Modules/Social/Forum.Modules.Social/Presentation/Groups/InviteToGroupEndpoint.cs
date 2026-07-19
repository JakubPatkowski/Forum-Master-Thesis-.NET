using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed record InviteToGroupRequest(string UserId);

internal sealed class InviteToGroupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/groups/{groupId}/invites", static async (
                string groupId,
                InviteToGroupRequest request,
                ICommandHandler<InviteToGroupCommand, InviteToGroupResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(groupId, CultureInfo.InvariantCulture, out var id)
                    || !Ulid.TryParse(request.UserId, CultureInfo.InvariantCulture, out var userId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new InviteToGroupCommand(id, userId), cancellationToken);
                return result.Match(static response =>
                    Results.Created($"/api/social/invites/{response.InviteId}", response));
            })
            .RequireAuthorization()
            .WithName("InviteToGroup")
            .WithTags("Social");
}
