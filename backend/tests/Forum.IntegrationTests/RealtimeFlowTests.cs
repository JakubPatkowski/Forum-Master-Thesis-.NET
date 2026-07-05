using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

using Forum.Api.Realtime;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.RabbitMq;
using Forum.Modules.Content.Contracts.IntegrationEvents;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using RabbitMQ.Client;

using Shouldly;

using Xunit;
using Xunit.Sdk;

namespace Forum.IntegrationTests;

/// <summary>
/// The Phase 7 WebSocket hub end to end against the real host + Postgres + RabbitMQ: ticket handshake,
/// subscribe/unsubscribe protocol, the full REST → outbox → relay → exchange → hub → socket path, the
/// fan-out-to-every-replica queue topology, per-push private-category authorization (including access revoked
/// mid-connection) and the reconnect-with-fresh-ticket/resync contract. Skipped when Docker is unavailable.
/// </summary>
public sealed class RealtimeFlowTests : IClassFixture<ForumApiFactory>
{
    private const string ModeratorRoleId = "00000000000000000000000002";
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(15);

    private readonly ForumApiFactory _factory;

    public RealtimeFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Changes_reach_a_subscribed_client_end_to_end_and_user_views_are_self_only()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "rt-alice");
        var bob = await RegisterAndLogin(client, "rt-bob");
        var categoryId = await CreateCategory(client, alice, "rt-e2e-cat");

        using var socket = await ConnectAsync(client, bob);
        await SubscribeAsync(socket, "category", categoryId);

        // A user view is self-only: watching someone else's activity stream is rejected.
        await SendJsonAsync(socket, new { action = "subscribe", view = "user", id = alice.UserId });
        var refusal = await ExpectFrameAsync(socket, static frame =>
            Field(frame, "type") == "error", "self-only user view refusal");
        Field(refusal, "reason").ShouldBe("forbidden-view");

        // Thread creation flows REST → outbox → relay → exchange → hub → socket as a compact envelope.
        var threadId = await CreateThread(client, alice, categoryId, "Live thread");
        var created = await ExpectFrameAsync(socket, frame =>
            Field(frame, "entity") == "thread" && Field(frame, "id") == threadId, "thread created push");
        Field(created, "type").ShouldBe("created");
        Field(created, "categoryId").ShouldBe(categoryId);
        created.TryGetProperty("version", out _).ShouldBeFalse("the envelope deliberately has no version field");

        // Comments carry the thread as parentId; the ack guarantees the view is live before the mutation.
        await SubscribeAsync(socket, "thread", threadId);
        var commentId = await CreateComment(client, alice, threadId, "First!");
        var comment = await ExpectFrameAsync(socket, frame =>
            Field(frame, "entity") == "comment" && Field(frame, "id") == commentId, "comment created push");
        Field(comment, "parentId").ShouldBe(threadId);

        // Reactions notify with the reacted target's id — the client patches that target's like count.
        await PutLike(client, alice, "thread", threadId);
        var reaction = await ExpectFrameAsync(socket, frame =>
            Field(frame, "entity") == "reaction", "reaction push");
        Field(reaction, "type").ShouldBe("created");
        Field(reaction, "id").ShouldBe(threadId);

        await CloseAsync(socket);
    }

    [SkippableFact]
    public async Task A_single_published_event_fans_out_to_every_replica()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var dave = await RegisterAndLogin(client, "rt-dave");
        var categoryId = await CreateCategory(client, dave, "rt-fanout-cat");

        // Replica 1: the host's own hub, observed through a subscribed socket.
        using var socket = await ConnectAsync(client, dave);
        await SubscribeAsync(socket, "category", categoryId);

        // Replica 2: a second, independently connected instance of the SAME feed service with a recording sink.
        var recorder = new RecordingSink();
        await using var connection = new TestRabbitMqConnection(_factory.RabbitMqConnectionString);
        var secondReplica = new RealtimeChangeFeedService(
            connection, recorder, Options.Create(new MessagingOptions()),
            NullLogger<RealtimeChangeFeedService>.Instance);
        await secondReplica.StartAsync(CancellationToken.None);
        try
        {
            // Its exclusive queue binds asynchronously — probe with raw events until one lands in the recorder.
            await TestWait.UntilAsync(
                async () =>
                {
                    await PublishProbeThreadCreatedAsync();
                    return !recorder.Notifications.IsEmpty;
                },
                "the second replica's queue is bound and consuming");

            // ONE real event; the topic exchange must fan it into BOTH replicas' queues (never competing-consumers).
            var threadId = await CreateThread(client, dave, categoryId, "Fanned out");

            var frame = await ExpectFrameAsync(socket, frame =>
                Field(frame, "entity") == "thread" && Field(frame, "id") == threadId, "replica 1 socket push");
            Field(frame, "type").ShouldBe("created");

            await TestWait.UntilAsync(
                () => Task.FromResult(recorder.Notifications.Any(
                    notification => notification.Payload.Id == threadId)),
                "replica 2's feed receives the same event");
        }
        finally
        {
            await secondReplica.StopAsync(CancellationToken.None);
        }

        await CloseAsync(socket);
    }

    [SkippableFact]
    public async Task Private_category_changes_are_never_pushed_to_unauthorized_subscribers()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var owner = await RegisterAndLogin(client, "rt-owner");
        var intruder = await RegisterAndLogin(client, "rt-intruder");
        var moderator = await RegisterAndLogin(client, "rt-moderator");

        var privateId = await CreateCategory(client, owner, "rt-private-cat", "private");
        var sentinelId = await CreateCategory(client, owner, "rt-sentinel-cat");

        // Both subscribe to the private AND a public sentinel category. Subscribing is always accepted —
        // authorization happens on every push, so what arrives (and what never does) is the assertion.
        using var intruderSocket = await ConnectAsync(client, intruder);
        await SubscribeAsync(intruderSocket, "category", privateId);
        await SubscribeAsync(intruderSocket, "category", sentinelId);

        await GrantRole(moderator.UserId, ModeratorRoleId);
        using var moderatorSocket = await ConnectAsync(client, moderator);
        await SubscribeAsync(moderatorSocket, "category", privateId);
        await SubscribeAsync(moderatorSocket, "category", sentinelId);

        // A private-category thread reaches the moderator but never the intruder (who never had access).
        var privateThread1 = await CreateThread(client, owner, privateId, "Members only");
        (await ExpectFrameAsync(moderatorSocket, frame => Field(frame, "id") == privateThread1,
            "moderator sees the private thread")).ShouldNotBe(default);

        var sentinel1 = await CreateThread(client, owner, sentinelId, "Public sentinel 1");
        var intruderFrames = await CollectFramesUntilAsync(intruderSocket, frame => Field(frame, "id") == sentinel1,
            "intruder receives the sentinel that was published after the private thread");
        intruderFrames.ShouldNotContain(frame => Field(frame, "id") == privateThread1,
            "the private thread must never reach a non-member");

        // Access revoked MID-CONNECTION: the very next push re-checks the SQL ACL and stops delivering.
        await RevokeRole(moderator.UserId, ModeratorRoleId);
        var privateThread2 = await CreateThread(client, owner, privateId, "Members only 2");
        var sentinel2 = await CreateThread(client, owner, sentinelId, "Public sentinel 2");

        var moderatorFrames = await CollectFramesUntilAsync(moderatorSocket, frame => Field(frame, "id") == sentinel2,
            "ex-moderator still receives public pushes");
        moderatorFrames.ShouldNotContain(frame => Field(frame, "id") == privateThread2,
            "a permission revoked mid-connection must gate the very next push");

        await CloseAsync(intruderSocket);
        await CloseAsync(moderatorSocket);
    }

    [SkippableFact]
    public async Task Reconnect_needs_a_fresh_single_use_ticket_and_resyncs_without_replay()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var carol = await RegisterAndLogin(client, "rt-carol");
        var categoryId = await CreateCategory(client, carol, "rt-reconnect-cat");

        var firstTicket = await MintTicket(client, carol);
        using (var socket = await ConnectAsync(firstTicket))
        {
            await SubscribeAsync(socket, "category", categoryId);
            var threadA = await CreateThread(client, carol, categoryId, "Before the drop");
            await ExpectFrameAsync(socket, frame => Field(frame, "id") == threadA, "push before disconnecting");
            await CloseAsync(socket);
        }

        // Tickets are single-use: the already-redeemed ticket cannot open a second socket.
        await ShouldRejectHandshakeAsync(firstTicket);

        // An event during the gap is simply gone from the hub's perspective — the client's resync re-fetches it.
        // Wait until the pipeline fully drained it (relayed + a broker-hop margin) while nobody is connected;
        // otherwise the still-in-flight delivery would legitimately land right after the resubscribe below.
        var threadGap = await CreateThread(client, carol, categoryId, "While disconnected");
        await TestWait.UntilAsync(
            () => IsThreadCreatedRelayedAsync(threadGap), "the gap event is relayed while disconnected");
        await Task.Delay(500);

        // A garbage ticket is rejected too; a fresh one connects cleanly with no server-side session to restore.
        await ShouldRejectHandshakeAsync("not-a-ticket");
        using var reconnected = await ConnectAsync(client, carol);
        await SubscribeAsync(reconnected, "category", categoryId);

        var threadB = await CreateThread(client, carol, categoryId, "After the reconnect");
        var frames = await CollectFramesUntilAsync(reconnected, frame => Field(frame, "id") == threadB,
            "the resubscribed socket receives new events");
        frames.ShouldNotContain(frame => Field(frame, "id") == threadGap,
            "no server-side replay/buffering: missed events are the resync's job");

        await CloseAsync(reconnected);
    }

    // ---- WebSocket helpers ----

    private async Task<WebSocket> ConnectAsync(HttpClient client, Session session) =>
        await ConnectAsync(await MintTicket(client, session));

    private async Task<WebSocket> ConnectAsync(string ticket)
    {
        var wsClient = _factory.Server.CreateWebSocketClient();
        var uri = new UriBuilder(_factory.Server.BaseAddress)
        {
            Scheme = "ws",
            Path = "/api/realtime/ws",
            Query = $"ticket={Uri.EscapeDataString(ticket)}",
        }.Uri;
        return await wsClient.ConnectAsync(uri, CancellationToken.None);
    }

    private async Task ShouldRejectHandshakeAsync(string ticket)
    {
        var rejected = false;
        try
        {
            using var socket = await ConnectAsync(ticket);
        }
        catch (Exception)
        {
            // TestServer surfaces the non-101 handshake response as an exception — exactly what we want.
            rejected = true;
        }

        rejected.ShouldBeTrue("the handshake must be rejected");
    }

    private static async Task SubscribeAsync(WebSocket socket, string view, string id)
    {
        await SendJsonAsync(socket, new { action = "subscribe", view, id });

        // Wait for the ack so the view is provably live before the test triggers the mutation.
        var ack = await ExpectFrameAsync(socket, frame =>
            Field(frame, "type") is "subscribed" or "error" && Field(frame, "id") == id, $"subscribe ack for {view}/{id}");
        Field(ack, "type").ShouldBe("subscribed");
    }

    private static Task SendJsonAsync(WebSocket socket, object message) =>
        socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text,
            endOfMessage: true, CancellationToken.None);

    private static async Task<JsonElement> ExpectFrameAsync(
        WebSocket socket, Func<JsonElement, bool> match, string because)
    {
        var frames = await CollectFramesUntilAsync(socket, match, because);
        return frames[^1];
    }

    /// <summary>Receives frames until one matches (returned last); throws with everything seen on timeout.</summary>
    private static async Task<IReadOnlyList<JsonElement>> CollectFramesUntilAsync(
        WebSocket socket, Func<JsonElement, bool> match, string because)
    {
        var frames = new List<JsonElement>();
        var deadline = DateTimeOffset.UtcNow + FrameTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var frame = await ReceiveJsonAsync(socket, deadline - DateTimeOffset.UtcNow);
            if (frame is null)
            {
                break;
            }

            frames.Add(frame.Value);
            if (match(frame.Value))
            {
                return frames;
            }
        }

        var seen = string.Join(", ", frames.Select(static frame => frame.GetRawText()));
        throw new XunitException($"Expected frame not received ({because}). Seen: [{seen}]");
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
                    return JsonSerializer.Deserialize<JsonElement>(message.ToArray());
                }
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static async Task CloseAsync(WebSocket socket)
    {
        if (socket.State == WebSocketState.Open)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
            catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
            {
                // Best-effort teardown; the server unregisters on any exit.
            }
        }
    }

    private static string? Field(JsonElement frame, string name) =>
        frame.TryGetProperty(name, out var property) ? property.GetString() : null;

    // ---- second-replica plumbing ----

    /// <summary>Records every notification the second replica's feed maps — its "sockets".</summary>
    private sealed class RecordingSink : IRealtimeNotificationSink
    {
        public ConcurrentBag<RealtimeNotification> Notifications { get; } = [];

        public Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    /// <summary>A second, independent broker connection — the test's "other pod".</summary>
    private sealed class TestRabbitMqConnection(string connectionString) : IRabbitMqConnection
    {
        private IConnection? _connection;

        public async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
        {
            if (_connection is not { IsOpen: true })
            {
                _connection = await new ConnectionFactory
                {
                    Uri = new Uri(connectionString),
                    AutomaticRecoveryEnabled = false,
                }.CreateConnectionAsync(cancellationToken);
            }

            return _connection;
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
    }

    private async Task PublishProbeThreadCreatedAsync()
    {
        var probe = new ThreadCreatedIntegrationEvent(
            Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), Ulid.NewUlid(), "probe", DateTimeOffset.UtcNow);
        var body = JsonSerializer.SerializeToUtf8Bytes(probe, IntegrationEventJson.SerializerOptions);

        var factory = new ConnectionFactory { Uri = new Uri(_factory.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync("content", ExchangeType.Topic, durable: true, autoDelete: false);
        await channel.BasicPublishAsync(
            "content", nameof(ThreadCreatedIntegrationEvent), mandatory: false,
            new BasicProperties { ContentType = "application/json" }, body);
    }

    // ---- database probes ----

    private Task GrantRole(string userId, string roleId) => ExecuteSqlAsync(
        "INSERT INTO forum_authz.user_roles (user_id, role_id) VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
        userId, roleId);

    private Task RevokeRole(string userId, string roleId) => ExecuteSqlAsync(
        "DELETE FROM forum_authz.user_roles WHERE user_id = {0} AND role_id = {1}", userId, roleId);

    private async Task ExecuteSqlAsync(string sql, params object[] parameters)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetServices<DbContext>().First();
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    /// <summary>True once the relay stamped the thread's ThreadCreated outbox row (publish confirmed by the broker).</summary>
    private async Task<bool> IsThreadCreatedRelayedAsync(string threadId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetServices<DbContext>().First();
        var connection = context.Database.GetDbConnection();
        await context.Database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM forum_content.outbox_messages " +
            "WHERE type = @type AND payload->>'ThreadId' = @threadId AND processed_on_utc IS NOT NULL";
        AddParameter(command, "@type", typeof(ThreadCreatedIntegrationEvent).FullName!);
        AddParameter(command, "@threadId", threadId);
        return (long)(await command.ExecuteScalarAsync())! == 1;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    // ---- flow helpers (same shape as the other suites) ----

    private static async Task<Session> RegisterAndLogin(HttpClient client, string username)
    {
        var email = $"{username}@example.com";
        const string password = "Password123!";

        var register = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { username, email, displayName = username, password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.UserId;

        var login = await client.PostAsJsonAsync("/api/identity/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var accessToken = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        return new Session(userId, accessToken);
    }

    private static async Task<string> MintTicket(HttpClient client, Session session)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/realtime/ticket");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TicketResponse>())!.Ticket;
    }

    private static async Task<string> CreateCategory(
        HttpClient client, Session session, string slug, string visibility = "public")
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/content/categories",
            new { slug, name = $"Category {slug}", description = "test", visibility });
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

    private static async Task<string> CreateComment(
        HttpClient client, Session session, string threadId, string body)
    {
        var response = await Send(client, session, HttpMethod.Post,
            $"/api/content/threads/{threadId}/comments", new { body });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CommentCreated>())!.CommentId;
    }

    private static async Task PutLike(HttpClient client, Session session, string targetType, string targetId)
    {
        var response = await Send(client, session, HttpMethod.Put,
            $"/api/engagement/reactions/{targetType}/{targetId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
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

    private sealed record Session(string UserId, string AccessToken);

    private sealed record RegisterResponse(string UserId);

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresOnUtc);

    private sealed record TicketResponse(string Ticket, int ExpiresInSeconds);

    private sealed record CategoryCreated(string CategoryId, string Slug);

    private sealed record ThreadCreated(string ThreadId);

    private sealed record CommentCreated(string CommentId);
}
