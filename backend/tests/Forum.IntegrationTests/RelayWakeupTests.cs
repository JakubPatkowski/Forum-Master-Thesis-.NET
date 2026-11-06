using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Shouldly;

using Xunit;
using Xunit.Sdk;

namespace Forum.IntegrationTests;

/// <summary>
/// The replica-local outbox wake-up (A/B v5, A4): with the relay's poll set to 5 s, a REST write still reaches the
/// realtime hub (socket push) and a downstream consumer (Engagement's ThreadDeleted handler) well under one second,
/// because the committing transaction signals the module's relay instead of waiting for the next poll. Own factory
/// (<c>Messaging:PollIntervalMilliseconds=5000</c>); skipped when Docker is unavailable.
/// </summary>
public sealed class RelayWakeupTests : IClassFixture<RelayWakeupTests.SlowPollFactory>
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(15);

    private readonly SlowPollFactory _factory;

    public RelayWakeupTests(SlowPollFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task A_write_reaches_the_hub_and_a_consumer_well_under_a_second_despite_a_five_second_poll()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "wk-alice");
        var categoryId = await CreateCategory(client, alice, "wk-cat");

        using var socket = await ConnectAsync(client, alice);
        await SubscribeAsync(socket, "category", categoryId);

        // The first write of the class also warms the relay's channel; measure the steady state on the second.
        var warm = await CreateThread(client, alice, categoryId, "warm-up");
        await ExpectFrameAsync(socket, frame => Field(frame, "id") == warm, "warm-up push");

        var pushLatencies = new List<TimeSpan>();
        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            var threadId = await CreateThread(client, alice, categoryId, $"Wake-up {i}");
            await ExpectFrameAsync(socket, frame => Field(frame, "id") == threadId, $"push for thread {i}");
            pushLatencies.Add(watch.Elapsed);
        }

        // Without the wake-up the relay would sleep up to 5 s between polls (median ≈ 2.5 s, as the v3/v4 J8 probes
        // measured); with it the push arrives within the broker hop.
        pushLatencies.Min().ShouldBeLessThan(TimeSpan.FromSeconds(1));
        pushLatencies.Count(latency => latency < TimeSpan.FromSeconds(1)).ShouldBeGreaterThanOrEqualTo(2,
            $"pushes must not wait for the 5 s poll (measured: {string.Join(", ", pushLatencies)})");

        // The consumer path (relay → exchange → Engagement's work queue → handler) wakes the same way.
        var doomed = await CreateThread(client, alice, categoryId, "Doomed");
        (await PutLike(client, alice, doomed)).ShouldBe(1);
        var consumerWatch = Stopwatch.StartNew();
        (await Send(client, alice, HttpMethod.Delete, $"/api/content/threads/{doomed}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        await TestWait.UntilAsync(
            async () => (await GetLikeCount(client, doomed)) == 0,
            "Engagement's ThreadDeleted consumer removes the reaction");
        consumerWatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1.5));

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    // ---- HTTP helpers ----

    private static async Task<Session> RegisterAndLogin(HttpClient client, string username)
    {
        var email = $"{username}@example.com";
        const string password = "Password123!";
        var register = await client.PostAsJsonAsync(
            "/api/identity/register", new { username, email, displayName = username, password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.UserId;
        var login = await client.PostAsJsonAsync("/api/identity/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return new Session(userId, (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken);
    }

    private static async Task<string> CreateCategory(HttpClient client, Session session, string slug)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/content/categories",
            new { slug, name = $"Category {slug}", description = "test", visibility = "public" });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CategoryCreated>())!.CategoryId;
    }

    private static async Task<string> CreateThread(HttpClient client, Session session, string categoryId, string title)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/content/threads",
            new { categoryId, title, body = $"Body of: {title}", tagSlugs = Array.Empty<string>() });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ThreadCreated>())!.ThreadId;
    }

    private static async Task<int> PutLike(HttpClient client, Session session, string threadId)
    {
        var response = await Send(client, session, HttpMethod.Put, $"/api/engagement/reactions/thread/{threadId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ReactionSummary>())!.Count;
    }

    private static async Task<int> GetLikeCount(HttpClient client, string threadId) =>
        (await client.GetFromJsonAsync<ReactionSummary>($"/api/engagement/reactions/thread/{threadId}"))!.Count;

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

    // ---- WebSocket helpers (the Phase 7 protocol, as in RealtimeFlowTests) ----

    private async Task<WebSocket> ConnectAsync(HttpClient client, Session session)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/realtime/ticket");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ticket = (await response.Content.ReadFromJsonAsync<TicketResponse>())!.Ticket;
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new UriBuilder(_factory.Server.BaseAddress)
        {
            Scheme = "ws",
            Path = "/api/realtime/ws",
            Query = $"ticket={Uri.EscapeDataString(ticket)}",
        }.Uri;
        return await wsClient.ConnectAsync(uri, CancellationToken.None);
    }

    private static async Task SubscribeAsync(WebSocket socket, string view, string id)
    {
        await socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(new { action = "subscribe", view, id }), WebSocketMessageType.Text,
            endOfMessage: true, CancellationToken.None);
        var ack = await ExpectFrameAsync(socket, frame =>
            Field(frame, "type") is "subscribed" or "error" && Field(frame, "id") == id, $"subscribe ack for {view}/{id}");
        Field(ack, "type").ShouldBe("subscribed");
    }

    private static async Task<JsonElement> ExpectFrameAsync(WebSocket socket, Func<JsonElement, bool> match, string because)
    {
        var seen = new List<string>();
        var deadline = DateTimeOffset.UtcNow + FrameTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var frame = await ReceiveJsonAsync(socket, deadline - DateTimeOffset.UtcNow);
            if (frame is null)
            {
                break;
            }

            seen.Add(frame.Value.GetRawText());
            if (match(frame.Value))
            {
                return frame.Value;
            }
        }

        throw new XunitException($"Expected frame not received ({because}). Seen: [{string.Join(", ", seen)}]");
    }

    private static async Task<JsonElement?> ReceiveJsonAsync(WebSocket socket, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        try
        {
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                message.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
    }

    private static string? Field(JsonElement frame, string name) =>
        frame.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public sealed class SlowPollFactory : ForumApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // The cluster's value — the floor every push paid before the wake-up (deviation D14).
            builder.UseSetting("Messaging:PollIntervalMilliseconds", "5000");
            builder.UseSetting("Messaging:WakeRelayOnWrite", "true");
        }
    }

    private sealed record Session(string UserId, string AccessToken);

    private sealed record RegisterResponse(string UserId);

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresOnUtc);

    private sealed record CategoryCreated(string CategoryId, string Slug);

    private sealed record ThreadCreated(string ThreadId);

    private sealed record TicketResponse(string Ticket);

    private sealed record ReactionSummary(int Count, bool ViewerReacted);
}
