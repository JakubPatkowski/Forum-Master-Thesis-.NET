using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Files.Application.Files;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Files.Presentation.Files;

internal sealed class InitiateUploadEndpoint : IEndpoint
{
    private sealed record InitiateUploadRequest(string ContentType, long SizeBytes);

    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/files", static async (
                InitiateUploadRequest request,
                ICommandHandler<InitiateUploadCommand, InitiateUploadResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(
                    new InitiateUploadCommand(request.ContentType ?? string.Empty, request.SizeBytes),
                    cancellationToken);

                return result.Match(static response => Results.Created(
                    $"/api/files/{response.FileId}",
                    new
                    {
                        fileId = response.FileId.ToString(),
                        objectKey = response.ObjectKey,
                        uploadUrl = response.UploadUrl,
                        method = "PUT",
                        expiresOnUtc = response.ExpiresOnUtc,
                    }));
            })
            .RequireAuthorization()
            .WithName("InitiateUpload")
            .WithTags("Files");
}
