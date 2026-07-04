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
/// DELETE = "make sure my like is gone from this target" — idempotent like its PUT counterpart; un-liking
/// something never liked still returns 200 with the current summary.
/// </summary>
internal sealed class RemoveReactionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/engagement/reactions/{targetType}/{targetId}", static async (
                string targetType,
                string targetId,
                ICommandHandler<RemoveReactionCommand, ReactionSummaryResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!ReactionTargets.TryParse(targetType, out var parsedType)
                    || !Ulid.TryParse(targetId, CultureInfo.InvariantCulture, out var parsedId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new RemoveReactionCommand(parsedType, parsedId), cancellationToken);
                return result.Match(static summary => Results.Ok(summary));
            })
            .RequireAuthorization()
            .WithName("RemoveReaction")
            .WithTags("Engagement");
}
