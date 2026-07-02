using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class UpdateThreadEndpoint : IEndpoint
{
    private sealed record UpdateThreadRequest(string Title, string Body);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/content/threads/{id}", static async (
                string id,
                UpdateThreadRequest request,
                ICommandHandler<UpdateThreadCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var threadId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new UpdateThreadCommand(threadId, request.Title, request.Body), cancellationToken);

                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("UpdateThread")
            .WithTags("Content");
}
