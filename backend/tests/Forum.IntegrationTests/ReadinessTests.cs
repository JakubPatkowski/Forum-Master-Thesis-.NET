using System.Net;

using Shouldly;

using Testcontainers.RabbitMq;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// Readiness gating on the messaging backbone: <c>/health/ready</c> flips to 503 while RabbitMQ is down and back
/// to 200 once it returns. Uses its own factory whose broker binds a fixed host port, so the restarted container
/// reappears at the endpoint the (already configured) host expects. Skipped when Docker is unavailable.
/// </summary>
public sealed class ReadinessTests : IClassFixture<ReadinessTests.FixedBrokerPortFactory>
{
    private readonly FixedBrokerPortFactory _factory;

    public ReadinessTests(FixedBrokerPortFactory factory) => _factory = factory;

    [SkippableFact]
    public async Task Readiness_flips_unhealthy_while_rabbitmq_is_down_and_recovers()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        using var client = _factory.CreateClient();

        // Both dependencies are up → ready.
        await TestWait.UntilAsync(
            async () => (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode
                        == HttpStatusCode.OK,
            "readiness reports healthy while Postgres and RabbitMQ are up");

        // Liveness never depends on downstream services.
        (await client.GetAsync(new Uri("/health/live", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await _factory.StopRabbitMqAsync();
        await TestWait.UntilAsync(
            async () => (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode
                        == HttpStatusCode.ServiceUnavailable,
            "readiness flips unhealthy once the broker is unreachable");

        await _factory.StartRabbitMqAsync();
        await TestWait.UntilAsync(
            async () => (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode
                        == HttpStatusCode.OK,
            "readiness recovers once the broker is back",
            timeoutSeconds: 60);
    }

    public sealed class FixedBrokerPortFactory : ForumApiFactory
    {
        protected override RabbitMqBuilder ConfigureRabbitMq(RabbitMqBuilder builder) =>
            builder.WithPortBinding(FreeTcpPort(), 5672);
    }
}
