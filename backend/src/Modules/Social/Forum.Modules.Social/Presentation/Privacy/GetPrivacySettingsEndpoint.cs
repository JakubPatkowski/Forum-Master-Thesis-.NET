using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Privacy;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Privacy;

internal sealed class GetPrivacySettingsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/privacy", static async (
                IQueryHandler<GetPrivacySettingsQuery, PrivacySettingsResponse> handler,
                CancellationToken cancellationToken) =>
            {
                var result = await handler.Handle(new GetPrivacySettingsQuery(), cancellationToken);
                return result.Match(static settings => Results.Ok(settings));
            })
            .RequireAuthorization()
            .WithName("GetPrivacySettings")
            .WithTags("Social");
}
