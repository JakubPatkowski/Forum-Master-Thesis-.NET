using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Comments;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Comments;

internal sealed class CreateCommentEndpoint : IEndpoint
{
    private sealed record CreateCommentRequest(string? ParentId, string Body);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/content/threads/{threadId}/comments", static async (
                string threadId,
                CreateCommentRequest request,
                ICommandHandler<CreateCommentCommand, CreateCommentResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(threadId, CultureInfo.InvariantCulture, out var parsedThreadId))
                {
                    return Results.NotFound();
                }

                Ulid? parentId = null;
                if (!string.IsNullOrWhiteSpace(request.ParentId))
                {
                    if (!Ulid.TryParse(request.ParentId, CultureInfo.InvariantCulture, out var parsedParentId))
                    {
                        return Results.NotFound();
                    }

                    parentId = parsedParentId;
                }

                var result = await handler.Handle(
                    new CreateCommentCommand(parsedThreadId, parentId, request.Body), cancellationToken);

                return result.Match(response => Results.Created(
                    $"/api/content/threads/{threadId}/comments",
                    new { commentId = response.CommentId.ToString() }));
            })
            .RequireAuthorization()
            .WithName("CreateComment")
            .WithTags("Content");
}
