using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Files.Application.Attachments;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Files.Presentation.Attachments;

internal sealed class AttachFileEndpoint : IEndpoint
{
    private sealed record AttachFileRequest(string TargetType, string? TargetId);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/files/{fileId}/attachments", static async (
                string fileId,
                AttachFileRequest request,
                ICommandHandler<AttachFileCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(fileId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                Ulid? targetId = null;
                if (!string.IsNullOrWhiteSpace(request.TargetId))
                {
                    if (!Ulid.TryParse(request.TargetId, CultureInfo.InvariantCulture, out var parsedTarget))
                    {
                        return Results.NotFound();
                    }

                    targetId = parsedTarget;
                }

                var result = await handler.Handle(
                    new AttachFileCommand(id, request.TargetType ?? string.Empty, targetId), cancellationToken);

                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("AttachFile")
            .WithTags("Files");
}
