using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Messaging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Messaging;

internal sealed class MarkConversationReadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/conversations/{conversationId}/read", static async (
                string conversationId,
                ICommandHandler<MarkConversationReadCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(conversationId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new MarkConversationReadCommand(id), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("MarkConversationRead")
            .WithTags("Social");
}
