using Forum.Common.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Content;

/// <summary>Content module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class ContentModule : IModule
{
    public string Name => "Content";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // TODO: ContentDbContext (schema "content"), repositories, Scrutor handler scan, validators.
        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: map IEndpoint implementations found in Presentation/.
        return endpoints;
    }
}
