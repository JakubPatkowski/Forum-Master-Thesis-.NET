using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed record TransferGroupOwnershipRequest(string UserId);

internal sealed class TransferGroupOwnershipEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/social/groups/{groupId}/owner", static async (
                string groupId,
                TransferGroupOwnershipRequest request,
                ICommandHandler<TransferGroupOwnershipCommand> handler,
                CancellationToken cancellationToken) =>
            {
                if (!Ulid.TryParse(groupId, CultureInfo.InvariantCulture, out var id)
                    || !Ulid.TryParse(request.UserId, CultureInfo.InvariantCulture, out var newOwnerId))
                {
                    return Results.NotFound();
                }

                var result = await handler.Handle(new TransferGroupOwnershipCommand(id, newOwnerId), cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("TransferGroupOwnership")
            .WithTags("Social");
}
