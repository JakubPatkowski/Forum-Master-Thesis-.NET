using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Files.Application.Files;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Files.Presentation.Files;

internal sealed class ListTargetFilesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/files", static async (
                string targetType,
                string targetId,
                IQueryHandler<ListTargetFilesQuery, IReadOnlyList<FileDownloadResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(targetId, CultureInfo.InvariantCulture, out var parsedTargetId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new ListTargetFilesQuery(targetType, parsedTargetId), cancellationToken);

                return result.Match(static responses =>
                    Results.Ok(responses.Select(GetFileEndpoint.ToPayload)));
            })
            .WithName("ListTargetFiles")
            .WithTags("Files");
}
