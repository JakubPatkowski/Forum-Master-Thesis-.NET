using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// Phase 9a end-to-end: after real traffic (login success/failure, thread/comment/reaction, a 404 rejection and
/// a full delete → outbox → RabbitMQ → consumer round-trip) every domain metric series must appear on
/// <c>/metrics</c> under the exact names the Phase 10c dashboards use, and the ProblemDetails rejection body must
/// echo the caller's correlation id. Skipped when Docker is unavailable.
/// </summary>
public sealed class ObservabilityFlowTests : IClassFixture<ForumApiFactory>
{
    private readonly ForumApiFactory _factory;

    public ObservabilityFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Domain_metrics_appear_on_the_prometheus_endpoint_after_real_traffic()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var user = await RegisterAndLogin(client, "obs-user");

        // One failed login for the invalid_credentials outcome (the response stays non-revealing).
        await client.PostAsJsonAsync(
            "/api/identity/login", new { email = "obs-user@example.com", password = "WrongPassword1!" });

        var categoryId = await CreateCategory(client, user, "obs-cat");
        var threadId = await CreateThread(client, user, categoryId, "Observed thread");
        await CreateComment(client, user, threadId, "observed comment");
        (await Send(client, user, HttpMethod.Put, $"/api/engagement/reactions/thread/{threadId}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Deleting the thread drives the full relay → consumer pipeline (Files + Engagement inbox rows).
        (await Send(client, user, HttpMethod.Delete, $"/api/content/threads/{threadId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The relay and consumers run on their own cadence — poll until their series materialize.
        // Also poll for auth attempts to account for Prometheus export lag.
        // GH Actions runs slower, so use 120s timeout; Prometheus exporter can stall under heavy load.
        await TestWait.UntilAsync(
            async () =>
            {
                var scraped = await client.GetStringAsync("/metrics");
                return scraped.Contains("forum_auth_attempts_total")
                    && scraped.Contains("forum_outbox_published_total")
                    && scraped.Contains("forum_messaging_consumed_total");
            },
            "metrics appear on /metrics endpoint (auth attempts, outbox relay, consumer results)",
            timeoutSeconds: 120);

        // GH Actions: second /metrics read can stall; retry if we get a partial response
        var metrics = string.Empty;
        await TestWait.UntilAsync(
            async () =>
            {
                metrics = await client.GetStringAsync("/metrics");
                return metrics.Contains("forum_auth_attempts_total");
            },
            "full metrics response (not partial target_info)",
            timeoutSeconds: 30);

        metrics.ShouldContain("forum_auth_attempts_total");
        metrics.ShouldContain("outcome=\"success\"");
        metrics.ShouldContain("outcome=\"invalid_credentials\"");
        metrics.ShouldContain("forum_threads_created_total");
        metrics.ShouldContain("forum_comments_created_total");
        metrics.ShouldContain("forum_reactions_total");
        metrics.ShouldContain("action=\"add\"");
        metrics.ShouldContain("forum_outbox_lag_seconds");
        metrics.ShouldContain("forum_hosted_service_tick_age_seconds");
        metrics.ShouldContain("outcome=\"ok\"");
    }

    [SkippableFact]
    public async Task A_rejected_request_echoes_the_correlation_id_in_header_and_body_and_is_counted()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/content/threads/{Ulid.NewUlid()}");
        request.Headers.Add("X-Correlation-ID", "obs-corr-404");
        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Headers.GetValues("X-Correlation-ID").ShouldHaveSingleItem().ShouldBe("obs-corr-404");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("correlationId").GetString().ShouldBe("obs-corr-404");
        body.RootElement.GetProperty("errorType").GetString().ShouldBe("NotFound");

        var metrics = await client.GetStringAsync("/metrics");
        metrics.ShouldContain("forum_api_rejections_total");
        metrics.ShouldContain("errorType=\"NotFound\"");
    }

    private static async Task<Session> RegisterAndLogin(HttpClient client, string username)
    {
        var email = $"{username}@example.com";
        const string password = "Password123!";

        var register = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { username, email, displayName = username, password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/identity/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var accessToken = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        return new Session(accessToken);
    }

    private static async Task<string> CreateCategory(HttpClient client, Session session, string slug)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/content/categories",
            new { slug, name = $"Category {slug}", description = "test", visibility = "public" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryCreated>())!.CategoryId;
    }

    private static async Task<string> CreateThread(
        HttpClient client, Session session, string categoryId, string title)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/content/threads",
            new { categoryId, title, body = $"Body of: {title}" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ThreadCreated>())!.ThreadId;
    }

    private static async Task CreateComment(HttpClient client, Session session, string threadId, string body)
    {
        var response = await Send(client, session, HttpMethod.Post,
            $"/api/content/threads/{threadId}/comments", new { body });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    private static Task<HttpResponseMessage> Send(
        HttpClient client, Session session, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }

    private sealed record Session(string AccessToken);

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresOnUtc);

    private sealed record CategoryCreated(string CategoryId, string Slug);

    private sealed record ThreadCreated(string ThreadId);
}
