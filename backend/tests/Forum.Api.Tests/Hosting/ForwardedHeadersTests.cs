using System.Net;

using Forum.Api.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Hosting;

/// <summary>
/// Forwarded-headers trust (G6): only proxies inside the configured CIDRs may rewrite the client IP.
/// With nothing configured (dev/compose) a spoofed X-Forwarded-For from the network must be ignored —
/// the rate limiter and request logs would otherwise attribute traffic to an attacker-chosen address.
/// </summary>
public sealed class ForwardedHeadersTests
{
    private const string SocketIpHeader = "X-Test-Socket-Ip";

    [Fact]
    public async Task A_spoofed_forwarded_header_from_an_untrusted_source_is_ignored()
    {
        await using var app = await BuildAppAsync();

        var clientIp = await SendAsync(app, socketIp: "203.0.113.7", forwardedFor: "1.2.3.4");

        clientIp.ShouldBe("203.0.113.7");
    }

    [Fact]
    public async Task A_forwarded_header_from_a_trusted_proxy_network_is_adopted()
    {
        await using var app = await BuildAppAsync("10.244.0.0/16");

        var clientIp = await SendAsync(app, socketIp: "10.244.1.20", forwardedFor: "198.51.100.9");

        clientIp.ShouldBe("198.51.100.9");
    }

    [Fact]
    public async Task A_source_outside_the_trusted_network_stays_untrusted()
    {
        await using var app = await BuildAppAsync("10.244.0.0/16");

        var clientIp = await SendAsync(app, socketIp: "10.245.0.9", forwardedFor: "198.51.100.9");

        clientIp.ShouldBe("10.245.0.9");
    }

    private static async Task<WebApplication> BuildAppAsync(params string[] knownNetworks)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(knownNetworks
            .Select(static (cidr, index) => new KeyValuePair<string, string?>(
                $"ForwardedHeaders:KnownNetworks:{index}", cidr)));
        builder.Services.AddForumForwardedHeaders(builder.Configuration);

        var app = builder.Build();

        // TestServer connections have no socket address — stamp one so the trust check has something to judge.
        app.Use(async (context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(context.Request.Headers[SocketIpHeader].ToString());
            await next(context);
        });
        app.UseForwardedHeaders();
        app.MapGet("/ip", static (HttpContext context) =>
            Results.Text(context.Connection.RemoteIpAddress!.ToString()));

        await app.StartAsync();
        return app;
    }

    private static async Task<string> SendAsync(WebApplication app, string socketIp, string forwardedFor)
    {
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ip");
        request.Headers.Add(SocketIpHeader, socketIp);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        var response = await client.SendAsync(request);
        return await response.Content.ReadAsStringAsync();
    }
}
