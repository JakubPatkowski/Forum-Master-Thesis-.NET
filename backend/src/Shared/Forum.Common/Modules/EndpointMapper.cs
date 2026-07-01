using System.Reflection;

using Microsoft.AspNetCore.Routing;

namespace Forum.Common.Modules;

/// <summary>Discovers and maps every <see cref="IEndpoint"/> in a module assembly (1 file = 1 endpoint).</summary>
public static class EndpointMapper
{
    public static IEndpointRouteBuilder MapEndpointsFrom(this IEndpointRouteBuilder app, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is { IsAbstract: false, IsInterface: false } && typeof(IEndpoint).IsAssignableFrom(type))
            {
                var endpoint = (IEndpoint)Activator.CreateInstance(type)!;
                endpoint.MapEndpoint(app);
            }
        }

        return app;
    }
}
