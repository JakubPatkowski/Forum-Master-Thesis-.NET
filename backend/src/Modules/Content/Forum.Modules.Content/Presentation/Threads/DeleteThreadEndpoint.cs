using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class DeleteThreadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/content/threads/{id}", static async (
                string id,
                ICommandHandler<DeleteThreadCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var threadId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new DeleteThreadCommand(threadId), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("DeleteThread")
            .WithTags("Content");
}
