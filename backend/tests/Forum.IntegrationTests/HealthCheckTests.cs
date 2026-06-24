using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>Smoke test: the whole Host boots and liveness responds, with no database/broker/storage available.</summary>
public sealed class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Liveness_endpoint_returns_200()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
