using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Categories;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Categories;

internal sealed class ListCategoriesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/categories", static async (
                IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new ListCategoriesQuery(), cancellationToken);
                return result.Match(static categories => Results.Ok(categories));
            })
            .AllowAnonymous()
            .WithName("ListCategories")
            .WithTags("Content");
}
