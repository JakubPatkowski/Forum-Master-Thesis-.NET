using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Content.Application;
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
                string? sort,
                string? cursor,
                int? limit,
                IQueryHandler<SearchThreadsQuery, CursorPage<ThreadFeedItemResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                // sort=relevance (default) ranks by ts_rank; sort=newest orders by created_on_utc DESC, id DESC. The
                // sort travels inside the cursor, so a page of the other order cannot be continued here (422).
                if (ThreadSearchSortParser.TryParse(sort) is not { } parsedSort)
                {
                    return ApiResults.Problem(ContentErrors.InvalidSearchSort);
                }

                var result = await handler.Handle(
                    new SearchThreadsQuery(q, parsedSort, cursor, limit ?? 20), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .AllowAnonymous()
            .WithName("SearchThreads")
            .WithTags("Content");
}
