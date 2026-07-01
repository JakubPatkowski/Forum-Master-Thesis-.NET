using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Administration;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Identity.Presentation.Administration;

internal sealed class ListUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/identity/admin/users", static async (
                string? cursor,
                int? limit,
                IQueryHandler<ListUsersQuery, CursorPage<UserSummaryResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new ListUsersQuery(cursor, limit ?? 20), cancellationToken);
                return result.Match(page => Results.Ok(page));
            })
            .RequirePermission(Permissions.Manage)
            .WithName("ListUsers")
            .WithTags("Identity.Admin");
}
