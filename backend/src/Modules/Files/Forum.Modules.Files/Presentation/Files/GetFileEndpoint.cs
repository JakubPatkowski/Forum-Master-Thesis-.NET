using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Files.Application.Files;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Files.Presentation.Files;

internal sealed class GetFileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/files/{fileId}", static async (
                string fileId,
                IQueryHandler<GetFileDownloadQuery, FileDownloadResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(fileId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new GetFileDownloadQuery(id), cancellationToken);

                return result.Match(static response => Results.Ok(ToPayload(response)));
            })
            .WithName("GetFile")
            .WithTags("Files");

    internal static object ToPayload(FileDownloadResponse response) => new
    {
        fileId = response.FileId.ToString(),
        url = response.Url,
        contentType = response.ContentType,
        sizeBytes = response.SizeBytes,
        width = response.Width,
        height = response.Height,
        expiresOnUtc = response.ExpiresOnUtc,
    };
}
