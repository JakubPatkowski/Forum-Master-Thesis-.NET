using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Messaging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Messaging;

internal sealed record SendMessageRequest(string Body);

internal sealed class SendMessageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/conversations/{conversationId}/messages", static async (
                string conversationId,
                SendMessageRequest request,
                ICommandHandler<SendMessageCommand, SendMessageResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(conversationId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new SendMessageCommand(id, request.Body ?? string.Empty), cancellationToken);
                return result.Match(static response => Results.Ok(response));
            })
            .RequireAuthorization()
            .WithName("SendMessage")
            .WithTags("Social");
}
