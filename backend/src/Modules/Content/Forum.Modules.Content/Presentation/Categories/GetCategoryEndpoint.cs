using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Categories;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Categories;

internal sealed class GetCategoryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/categories/{slug}", static async (
                string slug,
                IQueryHandler<GetCategoryQuery, CategoryResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetCategoryQuery(slug), cancellationToken);
                return result.Match(static category => Results.Ok(category));
            })
            .AllowAnonymous()
            .WithName("GetCategory")
            .WithTags("Content");
}
