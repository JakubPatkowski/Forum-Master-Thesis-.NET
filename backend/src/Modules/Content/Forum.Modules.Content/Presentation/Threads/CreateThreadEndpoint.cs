using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class CreateThreadEndpoint : IEndpoint
{
    private sealed record CreateThreadRequest(string CategoryId, string Title, string Body, string[]? TagSlugs);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/content/threads", static async (
                CreateThreadRequest request,
                ICommandHandler<CreateThreadCommand, CreateThreadResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(request.CategoryId, CultureInfo.InvariantCulture, out var categoryId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new CreateThreadCommand(categoryId, request.Title, request.Body, request.TagSlugs ?? []),
                    cancellationToken);

                return result.Match(static response => Results.Created(
                    $"/api/content/threads/{response.ThreadId}",
                    new { threadId = response.ThreadId.ToString() }));
            })
            .RequireAuthorization()
            .WithName("CreateThread")
            .WithTags("Content");
}
