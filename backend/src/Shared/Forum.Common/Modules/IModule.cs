using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Common.Modules;

/// <summary>A self-contained vertical module. The host discovers each one and wires it in.</summary>
public interface IModule
{
    string Name { get; }

    IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration);

    IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints);
}
