using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class PinThreadEndpoint : IEndpoint
{
    private sealed record PinThreadRequest(bool Pinned);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/content/threads/{id}/pin", static async (
                string id,
                PinThreadRequest request,
                ICommandHandler<PinThreadCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var threadId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new PinThreadCommand(threadId, request.Pinned), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("PinThread")
            .WithTags("Content");
}
