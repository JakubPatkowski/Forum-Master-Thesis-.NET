using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Files.Application.Attachments;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Files.Presentation.Attachments;

internal sealed class DetachFileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/files/{fileId}/attachments", static async (
                string fileId,
                string targetType,
                string? targetId,
                ICommandHandler<DetachFileCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(fileId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                Ulid? parsedTargetId = null;
                if (!string.IsNullOrWhiteSpace(targetId))
                {
                    if (!Ulid.TryParse(targetId, CultureInfo.InvariantCulture, out var parsedTarget))
                    {
                        return Results.NotFound();
                    }

                    parsedTargetId = parsedTarget;
                }

                var result = await handler.Handle(
                    new DetachFileCommand(id, targetType, parsedTargetId), cancellationToken);

                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("DetachFile")
            .WithTags("Files");
}
