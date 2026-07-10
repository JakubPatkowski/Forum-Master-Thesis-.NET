using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Tags;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Tags;

internal sealed class SuggestTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/tags", static async (
                string? query,
                int? limit,
                IQueryHandler<SuggestTagsQuery, IReadOnlyList<TagSuggestionResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new SuggestTagsQuery(query, limit ?? 20), cancellationToken);
                return result.Match(static tags => Results.Ok(tags));
            })
            .AllowAnonymous()
            .WithName("SuggestTags")
            .WithTags("Content");
}
