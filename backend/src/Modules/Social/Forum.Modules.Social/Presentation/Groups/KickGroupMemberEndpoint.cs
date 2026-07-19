using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed class KickGroupMemberEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/social/groups/{groupId}/members/{userId}", static async (
                string groupId,
                string userId,
                ICommandHandler<KickGroupMemberCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(groupId, CultureInfo.InvariantCulture, out var id)
                    || !Ulid.TryParse(userId, CultureInfo.InvariantCulture, out var memberId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new KickGroupMemberCommand(id, memberId), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("KickGroupMember")
            .WithTags("Social");
}
