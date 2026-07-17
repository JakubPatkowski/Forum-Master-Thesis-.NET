using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed record SetGroupMemberRoleRequest(string Role);

/// <summary>"admin" grants moderate at the group's ACL scope; "member" revokes it — the grant IS the role.</summary>
internal sealed class SetGroupMemberRoleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/social/groups/{groupId}/members/{userId}/role", static async (
                string groupId,
                string userId,
                SetGroupMemberRoleRequest request,
                ICommandHandler<SetGroupMemberRoleCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(groupId, CultureInfo.InvariantCulture, out var id)
                    || !Ulid.TryParse(userId, CultureInfo.InvariantCulture, out var memberId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(
                    new SetGroupMemberRoleCommand(id, memberId, request.Role ?? string.Empty), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("SetGroupMemberRole")
            .WithTags("Social");
}
