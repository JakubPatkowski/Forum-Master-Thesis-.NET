using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Threads;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class SearchThreadsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/search", static async (
                string? q,
                string? cursor,
                int? limit,
                IQueryHandler<SearchThreadsQuery, CursorPage<ThreadFeedItemResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new SearchThreadsQuery(q, cursor, limit ?? 20), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .AllowAnonymous()
            .WithName("SearchThreads")
            .WithTags("Content");
}
