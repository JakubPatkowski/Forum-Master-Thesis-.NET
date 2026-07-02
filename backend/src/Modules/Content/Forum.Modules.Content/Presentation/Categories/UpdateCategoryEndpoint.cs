using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Categories;
using Forum.SharedKernel.Results;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Categories;

internal sealed class UpdateCategoryEndpoint : IEndpoint
{
    private sealed record UpdateCategoryRequest(string Name, string? Description, string? Visibility);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/content/categories/{slug}", static async (
                string slug,
                UpdateCategoryRequest request,
                ICommandHandler<UpdateCategoryCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (VisibilityRequest.Parse(request.Visibility) is not { } visibility)
                {
                    return ApiResults.Problem(Error.Validation(
                        "category.invalid_visibility", "Visibility must be 'public' or 'private'."));
                }

                var result = await handler.Handle(
                    new UpdateCategoryCommand(slug, request.Name, request.Description ?? string.Empty, visibility),
                    cancellationToken);

                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("UpdateCategory")
            .WithTags("Content");
}
