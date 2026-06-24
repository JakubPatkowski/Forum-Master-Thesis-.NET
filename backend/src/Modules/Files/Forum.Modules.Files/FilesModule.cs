using Forum.Common.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Files;

/// <summary>Files module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class FilesModule : IModule
{
    public string Name => "Files";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // TODO: FilesDbContext (schema "files"), repositories, Scrutor handler scan, validators.
        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: map IEndpoint implementations found in Presentation/.
        return endpoints;
    }
}
