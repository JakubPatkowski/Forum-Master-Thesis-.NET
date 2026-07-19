using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Common.Paging;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Notifications;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Notifications;

internal sealed class GetNotificationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/notifications", static async (
                bool? unreadOnly,
                string? cursor,
                int? limit,
                IQueryHandler<GetNotificationsQuery, CursorPage<NotificationResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(
                    new GetNotificationsQuery(unreadOnly ?? false, cursor, limit), cancellationToken);
                return result.Match(static page => Results.Ok(page));
            })
            .RequireAuthorization()
            .WithName("GetNotifications")
            .WithTags("Social");
}
