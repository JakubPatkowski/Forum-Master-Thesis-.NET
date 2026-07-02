using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Comments;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Comments;

internal sealed class GetCommentTreeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/content/threads/{threadId}/comments", static async (
                string threadId,
                IQueryHandler<GetCommentTreeQuery, IReadOnlyList<CommentResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(threadId, CultureInfo.InvariantCulture, out var parsedThreadId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new GetCommentTreeQuery(parsedThreadId), cancellationToken);
                return result.Match(static comments => Results.Ok(comments));
            })
            .AllowAnonymous()
            .WithName("GetCommentTree")
            .WithTags("Content");
}
