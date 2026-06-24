using Forum.Common.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Identity;

/// <summary>Identity module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // TODO: IdentityDbContext (schema "identity"), repositories, Scrutor handler scan, validators.
        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: map IEndpoint implementations found in Presentation/.
        return endpoints;
    }
}
