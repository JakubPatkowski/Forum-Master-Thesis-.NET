using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Blocks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Blocks;

/// <summary>PUT = "make sure this user is blocked" — naturally idempotent.</summary>
internal sealed class BlockUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/social/blocks/{userId}", static async (
                string userId,
                ICommandHandler<BlockUserCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(userId, CultureInfo.InvariantCulture, out var blockedId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new BlockUserCommand(blockedId), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("BlockUser")
            .WithTags("Social");
}
