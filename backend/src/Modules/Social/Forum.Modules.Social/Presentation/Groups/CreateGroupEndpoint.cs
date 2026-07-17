using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Groups;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Groups;

internal sealed record CreateGroupRequest(string Name, string? Description, string? Visibility);

internal sealed class CreateGroupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/social/groups", static async (
                CreateGroupRequest request,
                ICommandHandler<CreateGroupCommand, CreateGroupResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var command = new CreateGroupCommand(
                    request.Name ?? string.Empty, request.Description ?? string.Empty,
                    request.Visibility ?? "public");
                var result = await handler.Handle(command, cancellationToken);
                return result.Match(static response =>
                    Results.Created($"/api/social/groups/{response.GroupId}", response));
            })
            .RequireAuthorization()
            .WithName("CreateGroup")
            .WithTags("Social");
}
