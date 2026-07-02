using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class ChangeThreadCategoryEndpoint : IEndpoint
{
    private sealed record ChangeCategoryRequest(string CategoryId);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/content/threads/{id}/category", static async (
                string id,
                ChangeCategoryRequest request,
                ICommandHandler<ChangeThreadCategoryCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var threadId)
                    || !Ulid.TryParse(request.CategoryId, CultureInfo.InvariantCulture, out var categoryId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new ChangeThreadCategoryCommand(threadId, categoryId), cancellationToken);

                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("ChangeThreadCategory")
            .WithTags("Content");
}
