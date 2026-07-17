using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Privacy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Privacy;

internal sealed record UpdatePrivacySettingsRequest(
    string FriendRequests, string Messages, string GroupInvites, bool ShowOnlineStatus);

internal sealed class UpdatePrivacySettingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/social/privacy", static async (
                UpdatePrivacySettingsRequest request,
                ICommandHandler<UpdatePrivacySettingsCommand> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new UpdatePrivacySettingsCommand(
                    request.FriendRequests ?? string.Empty,
                    request.Messages ?? string.Empty,
                    request.GroupInvites ?? string.Empty,
                    request.ShowOnlineStatus);
                var result = await handler.Handle(command, cancellationToken);
                return result.Match(static () => Results.NoContent());
            })
            .RequireAuthorization()
            .WithName("UpdatePrivacySettings")
            .WithTags("Social");
}
