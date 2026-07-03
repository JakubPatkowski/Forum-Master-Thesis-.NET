using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Files.Application.Files;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Files.Presentation.Files;

internal sealed class CommitUploadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/files/{fileId}/commit", static async (
                string fileId,
                ICommandHandler<CommitUploadCommand, CommitUploadResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(fileId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new CommitUploadCommand(id), cancellationToken);

                return result.Match(static response => Results.Ok(new
                {
                    fileId = response.FileId.ToString(),
                    contentType = response.ContentType,
                    sizeBytes = response.SizeBytes,
                    width = response.Width,
                    height = response.Height,
                }));
            })
            .RequireAuthorization()
            .WithName("CommitUpload")
            .WithTags("Files");
}
