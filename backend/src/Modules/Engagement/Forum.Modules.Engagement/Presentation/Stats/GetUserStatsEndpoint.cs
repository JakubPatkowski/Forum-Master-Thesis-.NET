using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Engagement.Application.Stats;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Engagement.Presentation.Stats;

internal sealed class GetUserStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/engagement/users/{userId}/stats", static async (
                string userId,
                IQueryHandler<GetUserStatsQuery, UserStatsResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(userId, CultureInfo.InvariantCulture, out var parsedUserId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new GetUserStatsQuery(parsedUserId), cancellationToken);
                return result.Match(static stats => Results.Ok(stats));
            })
            .AllowAnonymous()
            .WithName("GetUserStats")
            .WithTags("Engagement");
}
