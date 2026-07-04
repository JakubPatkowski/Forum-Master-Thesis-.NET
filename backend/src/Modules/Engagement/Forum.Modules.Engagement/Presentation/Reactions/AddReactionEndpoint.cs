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

/// <summary>
/// PUT = "make sure my like is on this target" — naturally idempotent, so a double-click or retry never
/// errors; the response always carries the current <c>{ count, viewerReacted }</c>.
/// </summary>
internal sealed class AddReactionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/engagement/reactions/{targetType}/{targetId}", static async (
                string targetType,
                string targetId,
                ICommandHandler<AddReactionCommand, ReactionSummaryResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!ReactionTargets.TryParse(targetType, out var parsedType)
                    || !Ulid.TryParse(targetId, CultureInfo.InvariantCulture, out var parsedId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new AddReactionCommand(parsedType, parsedId), cancellationToken);
                return result.Match(static summary => Results.Ok(summary));
            })
            .RequireAuthorization()
            .WithName("AddReaction")
            .WithTags("Engagement");
}
