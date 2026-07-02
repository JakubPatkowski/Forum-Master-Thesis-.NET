using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Content.Application.Comments;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Content.Presentation.Comments;

internal sealed class DeleteCommentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/content/comments/{id}", static async (
                string id,
                ICommandHandler<DeleteCommentCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(id, CultureInfo.InvariantCulture, out var commentId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new DeleteCommentCommand(commentId), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("DeleteComment")
            .WithTags("Content");
}
