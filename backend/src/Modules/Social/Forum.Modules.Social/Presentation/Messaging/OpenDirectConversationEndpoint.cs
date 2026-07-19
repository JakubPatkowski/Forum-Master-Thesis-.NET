using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Messaging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Messaging;

internal sealed record OpenDirectConversationRequest(string UserId);

/// <summary>Get-or-create is idempotent, so 200 with the id either way (no Created ceremony).</summary>
internal sealed class OpenDirectConversationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/conversations/direct", static async (
                OpenDirectConversationRequest request,
                ICommandHandler<OpenDirectConversationCommand, OpenDirectConversationResponse> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(request.UserId, CultureInfo.InvariantCulture, out var userId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new OpenDirectConversationCommand(userId), cancellationToken);
                return result.Match(static response => Results.Ok(response));
            })
            .RequireAuthorization()
            .WithName("OpenDirectConversation")
            .WithTags("Social");
}
