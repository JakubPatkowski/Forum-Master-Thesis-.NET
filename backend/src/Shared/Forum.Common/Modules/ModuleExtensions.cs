using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Common.Modules;

public static class ModuleExtensions
{
    public static IServiceCollection AddModules(
        this IServiceCollection services, IConfiguration configuration, IReadOnlyList<IModule> modules)
    {
        foreach (var module in modules)
        {
            module.RegisterServices(services, configuration);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app, IReadOnlyList<IModule> modules)
    {
        foreach (var module in modules)
        {
            module.MapEndpoints(app);
        }

        return app;
    }
}
