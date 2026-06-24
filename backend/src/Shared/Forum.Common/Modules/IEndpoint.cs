using Microsoft.AspNetCore.Routing;

namespace Forum.Common.Modules;

/// <summary>One REST endpoint (1 file = 1 endpoint). Discovered and mapped by its module.</summary>
public interface IEndpoint
{
    void MapEndpoint(IEndpointRouteBuilder app);
}
