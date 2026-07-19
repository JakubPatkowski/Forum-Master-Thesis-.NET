using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Notifications;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Notifications;

internal sealed class GetUnreadNotificationCountEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/notifications/unread-count", static async (
                IQueryHandler<GetUnreadNotificationCountQuery, UnreadCountResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetUnreadNotificationCountQuery(), cancellationToken);
                return result.Match(static count => Results.Ok(count));
            })
            .RequireAuthorization()
            .WithName("GetUnreadNotificationCount")
            .WithTags("Social");
}
