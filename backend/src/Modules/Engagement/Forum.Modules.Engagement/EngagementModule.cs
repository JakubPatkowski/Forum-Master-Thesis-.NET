using Forum.Common.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Engagement;

/// <summary>Engagement module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class EngagementModule : IModule
{
    public string Name => "Engagement";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // TODO: EngagementDbContext (schema "engagement"), repositories, Scrutor handler scan, validators.
        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: map IEndpoint implementations found in Presentation/.
        return endpoints;
    }
}
