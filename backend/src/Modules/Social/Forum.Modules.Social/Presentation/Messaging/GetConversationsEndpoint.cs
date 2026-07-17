using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Messaging;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Messaging;

internal sealed class GetConversationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/conversations", static async (
                IQueryHandler<GetConversationsQuery, IReadOnlyList<ConversationResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetConversationsQuery(), cancellationToken);
                return result.Match(static conversations => Results.Ok(conversations));
            })
            .RequireAuthorization()
            .WithName("GetConversations")
            .WithTags("Social");
}
