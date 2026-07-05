using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;

using Shouldly;

using Xunit;
using Xunit.Sdk;

namespace Forum.IntegrationTests;

/// <summary>
/// The Development-only monitor: the page is served with a relaxed CSP, and the passive bus tap streams every
/// relayed integration event without stealing deliveries from the real module consumers (whose effects the
/// other suites already assert). Skipped when Docker is unavailable.
/// </summary>
public sealed class DevMonitorTests : IClassFixture<ForumApiFactory>
{
    private readonly ForumApiFactory _factory;

    public DevMonitorTests(ForumApiFactory factory) => _factory = factory;

    [SkippableFact]
    public async Task Monitor_page_is_served_with_inline_friendly_csp()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var response = await _factory.CreateClient().GetAsync("/dev/monitor");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        (await response.Content.ReadAsStringAsync()).ShouldContain("Forum Dev Monitor");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("'unsafe-inline'");
    }

    [SkippableFact]
    public async Task Bus_tap_streams_a_relayed_integration_event()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new UriBuilder(_factory.Server.BaseAddress) { Scheme = "ws", Path = "/dev/monitor/bus" }.Uri;
        using var socket = await wsClient.ConnectAsync(uri, CancellationToken.None);

        var mona = await RegisterAndLogin(client, "monitor-mona");
        var categoryId = await CreateCategory(client, mona, "monitor-cat");
        var threadId = await CreateThread(client, mona, categoryId, "Observed thread");

        var frame = await ReceiveUntilAsync(socket, frame =>
            frame.GetProperty("kind").GetString() == "bus"
            && frame.GetProperty("routingKey").GetString() == "ThreadCreatedIntegrationEvent"
            && frame.GetProperty("body").GetProperty("ThreadId").GetString() == threadId);

        frame.GetProperty("exchange").GetString().ShouldBe("content");
        frame.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
        frame.GetProperty("body").GetProperty("CategoryId").GetString().ShouldBe(categoryId);
    }

    private static async Task<JsonElement> ReceiveUntilAsync(WebSocket socket, Func<JsonElement, bool> match)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        var buffer = new byte[16384];
        var seen = new List<string>();
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var cts = new CancellationTokenSource(deadline - DateTimeOffset.UtcNow);
            using var message = new MemoryStream();
            try
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, cts.Token);
                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var frame = JsonSerializer.Deserialize<JsonElement>(message.ToArray());
            if (match(frame))
            {
                return frame;
            }

            seen.Add(frame.GetRawText());
        }

        throw new XunitException($"Expected bus frame not received. Seen: [{string.Join(", ", seen)}]");
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
