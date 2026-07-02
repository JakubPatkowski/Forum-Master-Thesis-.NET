using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Threads;
using Forum.SharedKernel.Results;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Threads;

internal sealed class GetThreadFeedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/threads", static async (
                string? categoryId,
                string? cursor,
                int? limit,
                IQueryHandler<GetThreadFeedQuery, CursorPage<ThreadFeedItemResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(categoryId))
                {
                    return ApiResults.Problem(Error.Validation(
                        "feed.category_required", "The categoryId query parameter is required."));
                }

                if (!Ulid.TryParse(categoryId, CultureInfo.InvariantCulture, out var parsedCategoryId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new GetThreadFeedQuery(parsedCategoryId, cursor, limit ?? 20), cancellationToken);

                return result.Match(static page => Results.Ok(page));
            })
            .AllowAnonymous()
            .WithName("GetThreadFeed")
            .WithTags("Content");
}
