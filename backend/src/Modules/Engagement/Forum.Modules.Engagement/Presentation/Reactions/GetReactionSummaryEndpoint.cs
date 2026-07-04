using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Engagement.Application;
using Forum.Modules.Engagement.Application.Reactions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Engagement.Presentation.Reactions;

internal sealed class GetReactionSummaryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/engagement/reactions/{targetType}/{targetId}", static async (
                string targetType,
                string targetId,
                IQueryHandler<GetReactionSummaryQuery, ReactionSummaryResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!ReactionTargets.TryParse(targetType, out var parsedType)
                    || !Ulid.TryParse(targetId, CultureInfo.InvariantCulture, out var parsedId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new GetReactionSummaryQuery(parsedType, parsedId), cancellationToken);
                return result.Match(static summary => Results.Ok(summary));
            })
            .AllowAnonymous()
            .WithName("GetReactionSummary")
            .WithTags("Engagement");
}
