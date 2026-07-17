using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Presence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Presence;

/// <summary>The SPA beats every ~30 s while a tab is active; missing two beats reads as away/offline.</summary>
internal sealed class HeartbeatEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/presence/heartbeat", static async (
                ICommandHandler<HeartbeatCommand> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new HeartbeatCommand(), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("PresenceHeartbeat")
            .WithTags("Social");
}
