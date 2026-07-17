using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Notifications;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Notifications;

internal sealed record MarkNotificationsReadRequest(IReadOnlyList<string>? Ids);

/// <summary>No ids (or an empty list) marks everything unread as read.</summary>
internal sealed class MarkNotificationsReadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/notifications/read", static async (
                MarkNotificationsReadRequest request,
                ICommandHandler<MarkNotificationsReadCommand, MarkNotificationsReadResponse> handler,
                CancellationToken cancellationToken) =>
            {
                List<Ulid>? ids = null;
                if (request.Ids is { Count: > 0 })
                {
                    ids = new List<Ulid>(request.Ids.Count);
                    foreach (var raw in request.Ids)
                    {
                        if (!Ulid.TryParse(raw, CultureInfo.InvariantCulture, out var id))
                        {
                            return Results.NotFound();
                        }

                        ids.Add(id);
                    }
                }

                var result = await handler.Handle(new MarkNotificationsReadCommand(ids), cancellationToken);
                return result.Match(static response => Results.Ok(response));
            })
            .RequireAuthorization()
            .WithName("MarkNotificationsRead")
            .WithTags("Social");
}
