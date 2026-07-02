using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class GetThreadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/threads/{id}", static async (
                string id,
                IQueryHandler<GetThreadQuery, ThreadDetailResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var threadId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new GetThreadQuery(threadId), cancellationToken);
                return result.Match(static thread => Results.Ok(thread));
            })
            .AllowAnonymous()
            .WithName("GetThread")
            .WithTags("Content");
}
