using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Engagement.Application.Reactions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Engagement.Presentation.Reactions;

/// <summary>
/// Batch counts for a feed page: the SPA composes Content's feed with one call here instead of N single
/// lookups. Ids arrive comma-separated; validation (type, count, ULID shape) happens in the handler.
/// </summary>
internal sealed class GetReactionSummariesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/engagement/reactions/batch", static async (
                string? targetType,
                string? targetIds,
                IQueryHandler<GetReactionSummariesQuery, IReadOnlyList<ReactionSummaryResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var ids = (targetIds ?? string.Empty).Split(
                    ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var result = await handler.Handle(
                    new GetReactionSummariesQuery(targetType, ids), cancellationToken);
                return result.Match(static summaries => Results.Ok(summaries));
            })
            .AllowAnonymous()
            .WithName("GetReactionSummariesBatch")
            .WithTags("Engagement");
}
