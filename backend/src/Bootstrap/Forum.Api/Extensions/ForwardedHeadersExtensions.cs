using Microsoft.AspNetCore.HttpOverrides;

// The obsolete HttpOverrides.IPNetwork otherwise collides on the simple name.
using IPNetwork = System.Net.IPNetwork;

namespace Forum.Api.Extensions;

/// <summary>
/// X-Forwarded-For/-Proto handling for running behind ingress-nginx. Trust is explicit and config-driven:
/// only proxies inside the CIDRs listed under <c>ForwardedHeaders:KnownNetworks</c> are believed (the k8s
/// ConfigMap sets the pod network, e.g. <c>10.244.0.0/16</c>). With nothing configured the ASP.NET default —
/// loopback only — applies, so local dev and compose never trust a forwarded header spoofed from the network.
/// </summary>
public static class ForwardedHeadersExtensions
{
    public static IServiceCollection AddForumForwardedHeaders(
        this IServiceCollection services, IConfiguration configuration)
    {
        var knownNetworks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var cidr in knownNetworks)
            {
                options.KnownIPNetworks.Add(IPNetwork.Parse(cidr));
            }
        });

        return services;
    }
}
