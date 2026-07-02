using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Categories;
using Forum.SharedKernel.Results;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Categories;

internal sealed class CreateCategoryEndpoint : IEndpoint
{
    private sealed record CreateCategoryRequest(string Slug, string Name, string? Description, string? Visibility);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/content/categories", static async (
                CreateCategoryRequest request,
                ICommandHandler<CreateCategoryCommand, CreateCategoryResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (VisibilityRequest.Parse(request.Visibility) is not { } visibility)
                {
                    return ApiResults.Problem(Error.Validation(
                        "category.invalid_visibility", "Visibility must be 'public' or 'private'."));
                }

                var result = await handler.Handle(
                    new CreateCategoryCommand(request.Slug, request.Name, request.Description ?? string.Empty, visibility),
                    cancellationToken);

                return result.Match(static response => Results.Created(
                    $"/api/content/categories/{response.Slug}",
                    new { categoryId = response.CategoryId.ToString(), slug = response.Slug }));
            })
            .RequirePermission(Permissions.Create)
            .WithName("CreateCategory")
            .WithTags("Content");
}
