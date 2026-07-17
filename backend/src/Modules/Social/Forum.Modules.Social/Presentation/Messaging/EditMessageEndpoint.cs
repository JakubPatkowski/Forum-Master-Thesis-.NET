using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Messaging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Messaging;

internal sealed record EditMessageRequest(string Body);

internal sealed class EditMessageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/social/messages/{messageId}", static async (
                string messageId,
                EditMessageRequest request,
                ICommandHandler<EditMessageCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(messageId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new EditMessageCommand(id, request.Body ?? string.Empty), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("EditMessage")
            .WithTags("Social");
}
