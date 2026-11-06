using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;
using Xunit.Sdk;

namespace Forum.IntegrationTests;

/// <summary>
/// The Phase 11 Social module end to end against the real host + Postgres + MinIO + RabbitMQ (item 17 of
/// PHASE-11-SOCIAL-PROGRESS.md): friend request → accept → first DM creating the conversation with real
/// WebSocket pushes on <c>user</c>/<c>conversation</c> views; group create → invite → accept → chat with the
/// <c>group</c> view push (and the participant gate keeping non-members dark); the block and privacy denials
/// sharing one indistinguishable 403 per interaction kind; group-icon replace semantics + message-image
/// attachments with the participant-only read gate and detach-on-delete over the real outbox → broker →
/// consumer pipeline; presence heartbeat aging online → offline; and the live OpenAPI document listing the
/// full social surface. Skipped when Docker is unavailable.
/// </summary>
public sealed class SocialFlowTests : IClassFixture<ForumApiFactory>
{
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(15);

    private readonly ForumApiFactory _factory;

    public SocialFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Friend_request_accept_and_first_dm_flow_with_realtime_pushes()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "soc-alice");
        var bob = await RegisterAndLogin(client, "soc-bob");

        // Bob watches his own user view — the badge/bell path every social push can ride.
        using var bobSocket = await ConnectAsync(client, bob);
        await SubscribeAsync(bobSocket, "user", bob.UserId);

        // The request lands as a friendship push on the addressee's user view AND as a durable bell row.
        var friendshipId = await SendFriendRequest(client, alice, bob.UserId);
        var push = await ExpectFrameAsync(bobSocket, frame =>
            Field(frame, "entity") == "friendship" && Field(frame, "id") == friendshipId, "friend request push");
        Field(push, "type").ShouldBe("created");

        (await GetJson(client, bob, "/api/social/notifications/unread-count"))
            .GetProperty("unread").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
        var notifications = await GetJson(client, bob, "/api/social/notifications");
        notifications.GetProperty("items").EnumerateArray()
            .Any(static n => n.GetProperty("kind").GetString() == "friend.request").ShouldBeTrue();

        (await Send(client, bob, HttpMethod.Post, $"/api/social/friends/requests/{friendshipId}/accept"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var friends = await GetJson(client, alice, "/api/social/friends");
        friends.GetProperty("items").EnumerateArray()
            .Any(n => n.GetProperty("userId").GetString() == bob.UserId).ShouldBeTrue();

        // First DM: opening the conversation is get-or-create and idempotent from either side.
        var conversationId = await OpenDirectConversation(client, alice, bob.UserId);
        (await OpenDirectConversation(client, bob, alice.UserId)).ShouldBe(conversationId);

        // A direct message routes to the conversation view AND both participants' user views — Bob is
        // only subscribed to his user view and still gets the push (the multi-device badge path).
        var messageId = await SendMessage(client, alice, conversationId, "Hej Bob!");
        var messagePush = await ExpectFrameAsync(bobSocket, frame =>
            Field(frame, "entity") == "message" && Field(frame, "id") == messageId, "dm push on the user view");
        Field(messagePush, "type").ShouldBe("created");
        Field(messagePush, "parentId").ShouldBe(conversationId);
        // Social envelopes have no category: the serializer drops the null, so the key must be absent.
        messagePush.TryGetProperty("categoryId", out _).ShouldBeFalse();

        var history = await GetJson(client, bob, $"/api/social/conversations/{conversationId}/messages");
        history.GetProperty("items").EnumerateArray()
            .Any(m => m.GetProperty("messageId").GetString() == messageId).ShouldBeTrue();

        // The unread badge is the reader's own read marker: nonzero until Bob stamps the conversation read.
        UnreadOf(await GetJson(client, bob, "/api/social/conversations"), conversationId)
            .ShouldBeGreaterThanOrEqualTo(1);
        (await Send(client, bob, HttpMethod.Post, $"/api/social/conversations/{conversationId}/read"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        UnreadOf(await GetJson(client, bob, "/api/social/conversations"), conversationId).ShouldBe(0);

        await CloseAsync(bobSocket);
    }

    [SkippableFact]
    public async Task Group_invite_accept_and_chat_push_on_the_group_view_gated_to_members()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var grace = await RegisterAndLogin(client, "soc-grace");
        var heidi = await RegisterAndLogin(client, "soc-heidi");
        var mallory = await RegisterAndLogin(client, "soc-mallory");

        var groupId = await CreateGroup(client, grace, "Grace's book club", "private");
        var inviteId = await InviteToGroup(client, grace, groupId, heidi.UserId);

        var invites = await GetJson(client, heidi, "/api/social/invites");
        invites.EnumerateArray().Any(i => i.GetProperty("inviteId").GetString() == inviteId).ShouldBeTrue();
        (await Send(client, heidi, HttpMethod.Post, $"/api/social/invites/{inviteId}/accept"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var members = await GetJson(client, grace, $"/api/social/groups/{groupId}/members");
        members.GetProperty("items").GetArrayLength().ShouldBe(2);

        // Subscribing to a group view is always accepted — authorization happens per push, so the member
        // receives the chat message while the non-member's identical subscription stays dark.
        using var heidiSocket = await ConnectAsync(client, heidi);
        await SubscribeAsync(heidiSocket, "group", groupId);
        using var mallorySocket = await ConnectAsync(client, mallory);
        await SubscribeAsync(mallorySocket, "group", groupId);
        await SubscribeAsync(mallorySocket, "user", mallory.UserId);

        // The group's conversation id IS the group id — chat needs no discovery round-trip.
        var messageId = await SendMessage(client, grace, groupId, "Welcome to the club!");
        var push = await ExpectFrameAsync(heidiSocket, frame =>
            Field(frame, "entity") == "message" && Field(frame, "id") == messageId, "group chat push to a member");
        Field(push, "parentId").ShouldBe(groupId);

        // Sentinel bound for the negative assert: a friend request push mallory WILL receive on her user view.
        await SendFriendRequest(client, grace, mallory.UserId);
        var malloryFrames = await CollectFramesUntilAsync(mallorySocket, frame =>
            Field(frame, "entity") == "friendship", "mallory receives the sentinel friendship push");
        malloryFrames.ShouldNotContain(frame => Field(frame, "id") == messageId,
            "group chat must never reach a non-member, subscribed or not");

        // The owner can never leave their own group — transfer or delete are the only exits.
        (await Send(client, grace, HttpMethod.Post, $"/api/social/groups/{groupId}/leave"))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await CloseAsync(heidiSocket);
        await CloseAsync(mallorySocket);
    }

    [SkippableFact]
    public async Task Blocks_and_privacy_denials_share_one_indistinguishable_403_per_interaction()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var carl = await RegisterAndLogin(client, "soc-carl");
        var dana = await RegisterAndLogin(client, "soc-dana");
        var erin = await RegisterAndLogin(client, "soc-erin");

        // Carl blocks Dana: she can neither friend-request nor DM him — and the block never reveals itself.
        (await Send(client, carl, HttpMethod.Put, $"/api/social/blocks/{dana.UserId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var blockedRequest = await Send(client, dana, HttpMethod.Post, "/api/social/friends/requests",
            new { addresseeId = carl.UserId });
        blockedRequest.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var blockedRequestCode = await ReadProblemCode(blockedRequest);

        var blockedDm = await Send(client, dana, HttpMethod.Post, "/api/social/conversations/direct",
            new { userId = carl.UserId });
        blockedDm.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var blockedDmCode = await ReadProblemCode(blockedDm);

        // A block suppresses DMs in BOTH directions — the blocker messaging their blocked party is nonsense UX.
        (await Send(client, carl, HttpMethod.Post, "/api/social/conversations/direct",
            new { userId = dana.UserId })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Privacy denials produce the SAME codes as block denials — that is the indistinguishability contract.
        await UpdatePrivacy(client, erin, friendRequests: "no_one", messages: "friends");
        var privacyRequest = await Send(client, dana, HttpMethod.Post, "/api/social/friends/requests",
            new { addresseeId = erin.UserId });
        privacyRequest.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadProblemCode(privacyRequest)).ShouldBe(blockedRequestCode);

        var privacyDm = await Send(client, dana, HttpMethod.Post, "/api/social/conversations/direct",
            new { userId = erin.UserId });
        privacyDm.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReadProblemCode(privacyDm)).ShouldBe(blockedDmCode);

        // The friends audience opens the gate through a real friendship: request → accept → the DM now flows.
        await UpdatePrivacy(client, erin, friendRequests: "everyone", messages: "friends");
        var friendshipId = await SendFriendRequest(client, dana, erin.UserId);
        (await Send(client, erin, HttpMethod.Post, $"/api/social/friends/requests/{friendshipId}/accept"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Send(client, dana, HttpMethod.Post, "/api/social/conversations/direct",
            new { userId = erin.UserId })).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Unblocking restores the friend-request path for Dana → Carl too.
        (await Send(client, carl, HttpMethod.Delete, $"/api/social/blocks/{dana.UserId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Send(client, dana, HttpMethod.Post, "/api/social/friends/requests",
            new { addresseeId = carl.UserId })).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [SkippableFact]
    public async Task Group_icon_replaces_message_images_are_participant_gated_and_detach_on_delete()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var gina = await RegisterAndLogin(client, "soc-gina");
        var hank = await RegisterAndLogin(client, "soc-hank");
        var eve = await RegisterAndLogin(client, "soc-eve");

        var groupId = await CreateGroup(client, gina, "Icon testers", "public");
        var inviteId = await InviteToGroup(client, gina, groupId, hank.UserId);
        (await Send(client, hank, HttpMethod.Post, $"/api/social/invites/{inviteId}/accept"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Group icon: the avatar-style replace arm — attaching a second icon displaces the first.
        var icon1 = await UploadAndCommit(client, gina);
        var icon2 = await UploadAndCommit(client, gina);
        await Attach(client, gina, icon1, "group_icon", groupId);
        await Attach(client, gina, icon2, "group_icon", groupId);
        var icons = await ListFiles(client, "group_icon", groupId);
        icons.GetArrayLength().ShouldBe(1);
        icons[0].GetProperty("fileId").GetString().ShouldBe(icon2);

        // Message images: additive, sender-only to attach, participant-only to read.
        var messageId = await SendMessage(client, gina, groupId, "Two screenshots attached");
        var image1 = await UploadAndCommit(client, gina);
        var image2 = await UploadAndCommit(client, gina);
        await Attach(client, gina, image1, "message", messageId);
        await Attach(client, gina, image2, "message", messageId);

        (await ListFiles(client, "message", messageId, hank)).GetArrayLength().ShouldBe(2);
        (await Send(client, eve, HttpMethod.Get, $"/api/files?targetType=message&targetId={messageId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/files?targetType=message&targetId={messageId}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(client, eve, HttpMethod.Get, $"/api/files/{image1}"))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Send(client, hank, HttpMethod.Get, $"/api/files/{image1}"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Deleting the message detaches its images through the real outbox → broker → Files consumer path.
        (await Send(client, gina, HttpMethod.Delete, $"/api/social/messages/{messageId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await TestWait.UntilAsync(
            async () => (await ListFiles(client, "message", messageId, gina)).GetArrayLength() == 0,
            "Files' MessageDeleted consumer detaches the message's images");
    }

    [SkippableFact]
    public async Task Presence_heartbeat_reads_online_then_ages_out_to_offline()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var ivy = await RegisterAndLogin(client, "soc-ivy");
        var judy = await RegisterAndLogin(client, "soc-judy");

        (await Send(client, judy, HttpMethod.Post, "/api/social/presence/heartbeat"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await PresenceOf(client, ivy, judy.UserId)).ShouldBe("online");

        // Never-heartbeated users read as offline (indistinguishable from hidden ones — by design).
        (await PresenceOf(client, judy, ivy.UserId)).ShouldBe("offline");

        // Age the heartbeat past every threshold instead of sleeping through one — deterministic and instant.
        await ExecuteSqlAsync(
            "UPDATE forum_social.user_presence SET last_heartbeat_on_utc = now() - interval '1 hour' " +
            "WHERE user_id = {0}", judy.UserId);
        (await PresenceOf(client, ivy, judy.UserId)).ShouldBe("offline");
    }

    [SkippableFact]
    public async Task Openapi_document_lists_the_full_social_surface()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        // 30 route templates / 38 operations — the numbers CLAUDE.md's "26 endpoints" phrasing predates.
        string[] expectedPaths =
        [
            "/api/social/blocks", "/api/social/blocks/{userId}",
            "/api/social/conversations", "/api/social/conversations/direct",
            "/api/social/conversations/{conversationId}/messages", "/api/social/conversations/{conversationId}/read",
            "/api/social/friends", "/api/social/friends/requests",
            "/api/social/friends/requests/{friendshipId}", "/api/social/friends/requests/{friendshipId}/accept",
            "/api/social/friends/{userId}",
            "/api/social/groups", "/api/social/groups/{groupId}", "/api/social/groups/{groupId}/invites",
            "/api/social/groups/{groupId}/join", "/api/social/groups/{groupId}/leave",
            "/api/social/groups/{groupId}/members", "/api/social/groups/{groupId}/members/{userId}",
            "/api/social/groups/{groupId}/members/{userId}/role", "/api/social/groups/{groupId}/owner",
            "/api/social/invites", "/api/social/invites/{inviteId}", "/api/social/invites/{inviteId}/accept",
            "/api/social/messages/{messageId}",
            "/api/social/notifications", "/api/social/notifications/read", "/api/social/notifications/unread-count",
            "/api/social/presence", "/api/social/presence/heartbeat",
            "/api/social/privacy",
        ];

        var client = CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var socialPaths = document.GetProperty("paths").EnumerateObject()
            .Where(static p => p.Name.StartsWith("/api/social/", StringComparison.Ordinal))
            .ToDictionary(static p => p.Name, static p => p.Value.EnumerateObject().Count());

        socialPaths.Keys.OrderBy(static p => p, StringComparer.Ordinal)
            .ShouldBe(expectedPaths.OrderBy(static p => p, StringComparer.Ordinal));
        socialPaths.Values.Sum().ShouldBe(38);
    }

    // ---- social flow helpers ----

    private static async Task<string> SendFriendRequest(HttpClient client, Session from, string addresseeId)
    {
        var response = await Send(client, from, HttpMethod.Post, "/api/social/friends/requests",
            new { addresseeId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("friendshipId").GetString()!;
    }

    private static async Task<string> OpenDirectConversation(HttpClient client, Session session, string userId)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/social/conversations/direct",
            new { userId });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("conversationId").GetString()!;
    }

    private static async Task<string> SendMessage(
        HttpClient client, Session session, string conversationId, string body)
    {
        var response = await Send(client, session, HttpMethod.Post,
            $"/api/social/conversations/{conversationId}/messages", new { body });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("messageId").GetString()!;
    }

    private static async Task<string> CreateGroup(
        HttpClient client, Session session, string name, string visibility)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/social/groups",
            new { name, description = "integration test group", visibility });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groupId").GetString()!;
    }

    private static async Task<string> InviteToGroup(
        HttpClient client, Session session, string groupId, string userId)
    {
        var response = await Send(client, session, HttpMethod.Post, $"/api/social/groups/{groupId}/invites",
            new { userId });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("inviteId").GetString()!;
    }

    private static async Task UpdatePrivacy(
        HttpClient client, Session session, string friendRequests, string messages)
    {
        var response = await Send(client, session, HttpMethod.Put, "/api/social/privacy",
            new { friendRequests, messages, groupInvites = "everyone", showOnlineStatus = true });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<string> PresenceOf(HttpClient client, Session viewer, string userId)
    {
        var entries = await GetJson(client, viewer, $"/api/social/presence?userIds={userId}");
        return entries.EnumerateArray().Single().GetProperty("status").GetString()!;
    }

    private static int UnreadOf(JsonElement conversations, string conversationId) =>
        conversations.EnumerateArray()
            .Single(c => c.GetProperty("conversationId").GetString() == conversationId)
            .GetProperty("unreadCount").GetInt32();

    // ---- files helpers (FilesFlowTests shapes) ----

    private static async Task<string> UploadAndCommit(HttpClient client, Session session)
    {
        var png = PngBytes(16, 16);
        var initiate = await Send(client, session, HttpMethod.Post, "/api/files",
            new { contentType = "image/png", sizeBytes = png.Length });
        initiate.StatusCode.ShouldBe(HttpStatusCode.Created);
        var initiated = await initiate.Content.ReadFromJsonAsync<JsonElement>();

        using (var http = new HttpClient())
        using (var content = new ByteArrayContent(png))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            (await http.PutAsync(new Uri(initiated.GetProperty("uploadUrl").GetString()!), content))
                .EnsureSuccessStatusCode();
        }

        var fileId = initiated.GetProperty("fileId").GetString()!;
        (await Send(client, session, HttpMethod.Post, $"/api/files/{fileId}/commit"))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        return fileId;
    }

    private static async Task Attach(
        HttpClient client, Session session, string fileId, string targetType, string targetId)
    {
        var response = await Send(client, session, HttpMethod.Post, $"/api/files/{fileId}/attachments",
            new { targetType, targetId });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private static async Task<JsonElement> ListFiles(
        HttpClient client, string targetType, string targetId, Session? session = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/files?targetType={targetType}&targetId={targetId}");
        if (session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static byte[] PngBytes(int width, int height)
    {
        var bytes = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    // ---- WebSocket helpers (RealtimeFlowTests shapes) ----

    private async Task<WebSocket> ConnectAsync(HttpClient client, Session session)
    {
        var ticketResponse = await Send(client, session, HttpMethod.Post, "/api/realtime/ticket");
        ticketResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ticket = (await ticketResponse.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("ticket").GetString()!;

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
            JsonSerializer.SerializeToUtf8Bytes(new { action = "subscribe", view, id }),
            WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        var ack = await ExpectFrameAsync(socket, frame =>
            Field(frame, "type") is "subscribed" or "error" && Field(frame, "id") == id, $"subscribe ack for {view}/{id}");
        Field(ack, "type").ShouldBe("subscribed");
    }

    private static async Task<JsonElement> ExpectFrameAsync(
        WebSocket socket, Func<JsonElement, bool> match, string because)
    {
        var frames = await CollectFramesUntilAsync(socket, match, because);
        return frames[^1];
    }

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

    // ---- shared plumbing (same shape as the other suites) ----

    private async Task ExecuteSqlAsync(string sql, params object[] parameters)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetServices<DbContext>().First();
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private static async Task<Session> RegisterAndLogin(HttpClient client, string username)
    {
        var email = $"{username}@example.com";
        const string password = "Password123!";

        var register = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { username, email, displayName = username, password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await register.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString()!;

        var login = await client.PostAsJsonAsync("/api/identity/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var accessToken = (await login.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("accessToken").GetString()!;

        return new Session(userId, accessToken);
    }

    private static async Task<JsonElement> GetJson(HttpClient client, Session session, string url)
    {
        var response = await Send(client, session, HttpMethod.Get, url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
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
}
