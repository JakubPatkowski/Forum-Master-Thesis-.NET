using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class GetUserThreadsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/users/{userId}/threads", static async (
                string userId,
                string? cursor,
                int? limit,
                IQueryHandler<GetUserThreadsQuery, CursorPage<ThreadFeedItemResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(userId, CultureInfo.InvariantCulture, out var ownerId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new GetUserThreadsQuery(ownerId, cursor, limit ?? 20), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .AllowAnonymous()
            .WithName("GetUserThreads")
            .WithTags("Content");
}
