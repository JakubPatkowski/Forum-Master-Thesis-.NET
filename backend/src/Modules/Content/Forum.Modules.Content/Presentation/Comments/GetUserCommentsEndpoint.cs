using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Content.Application.Comments;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Comments;

internal sealed class GetUserCommentsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/users/{userId}/comments", static async (
                string userId,
                string? cursor,
                int? limit,
                IQueryHandler<GetUserCommentsQuery, CursorPage<CommentActivityItemResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(userId, CultureInfo.InvariantCulture, out var ownerId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new GetUserCommentsQuery(ownerId, cursor, limit ?? 20), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .AllowAnonymous()
            .WithName("GetUserComments")
            .WithTags("Content");
}
