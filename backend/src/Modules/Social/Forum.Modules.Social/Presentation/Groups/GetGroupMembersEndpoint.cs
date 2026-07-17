using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed class GetGroupMembersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/groups/{groupId}/members", static async (
                string groupId,
                string? cursor,
                int? limit,
                IQueryHandler<GetGroupMembersQuery, CursorPage<GroupMemberResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(groupId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new GetGroupMembersQuery(id, cursor, limit), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .RequireAuthorization()
            .WithName("GetGroupMembers")
            .WithTags("Social");
}
