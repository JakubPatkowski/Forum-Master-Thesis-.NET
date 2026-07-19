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

internal sealed class GetGroupsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/groups", static async (
                string? filter,
                string? cursor,
                int? limit,
                IQueryHandler<GetGroupsQuery, CursorPage<GroupSummaryResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetGroupsQuery(filter, cursor, limit), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .RequireAuthorization()
            .WithName("GetGroups")
            .WithTags("Social");
}
