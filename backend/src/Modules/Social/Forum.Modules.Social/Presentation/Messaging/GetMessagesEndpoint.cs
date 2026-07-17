using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Messaging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Messaging;

internal sealed class GetMessagesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/conversations/{conversationId}/messages", static async (
                string conversationId,
                string? cursor,
                int? limit,
                IQueryHandler<GetMessagesQuery, CursorPage<MessageResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(conversationId, CultureInfo.InvariantCulture, out var id))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new GetMessagesQuery(id, cursor, limit), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .RequireAuthorization()
            .WithName("GetMessages")
            .WithTags("Social");
}
