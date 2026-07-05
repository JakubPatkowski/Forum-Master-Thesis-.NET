using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Forum.Infrastructure.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RabbitMQ.Client;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// The Phase 6 messaging backbone, end to end against the real host + Postgres + RabbitMQ: a state change's
/// outbox row is relayed to the source module's topic exchange and consumed by the downstream modules' real
/// handlers (no test ever invokes a handler directly), duplicate deliveries are structurally deduped by the
/// inbox, and a malformed message is parked in the poison queue without blocking the flow. Skipped when Docker
/// is unavailable.
/// </summary>
public sealed class MessagingFlowTests : IClassFixture<ForumApiFactory>
{
    private const string ThreadDeletedType =
        "Forum.Modules.Content.Contracts.IntegrationEvents.ThreadDeletedIntegrationEvent";

    private readonly ForumApiFactory _factory;

    public MessagingFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Thread_deletion_flows_from_outbox_through_rabbitmq_to_consumers()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "msg-alice");
        var bob = await RegisterAndLogin(client, "msg-bob");
        var categoryId = await CreateCategory(client, alice, "msg-e2e-cat");
        var threadId = await CreateThread(client, alice, categoryId, "Doomed by the bus");
        (await PutLike(client, bob, "thread", threadId)).Count.ShouldBe(1);

        var delete = await Send(client, alice, HttpMethod.Delete, $"/api/content/threads/{threadId}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var correlationId = delete.Headers.GetValues("X-Correlation-ID").Single();

        // The real pipeline (relay → 'content' exchange → consumer host → registered handler) removes the like.
        await TestWait.UntilAsync(
            async () => (await GetSummary(client, "thread", threadId)).Count == 0,
            "Engagement's ThreadDeleted consumer removes the thread's reactions");

        // And left its audit trail: the outbox row is stamped processed and carries the request's correlation id…
        var outbox = await FindThreadDeletedOutboxRowAsync(threadId);
        outbox.ShouldNotBeNull();
        outbox.Processed.ShouldBeTrue();
        outbox.CorrelationId.ShouldBe(correlationId);

        // …and BOTH consuming modules recorded the event in their inbox (Files no-ops: nothing was attached).
        await TestWait.UntilAsync(
            async () => await InboxCountAsync("forum_engagement", outbox.EventId) == 1
                        && await InboxCountAsync("forum_files", outbox.EventId) == 1,
            "both consuming modules record the event in their inbox tables");
    }

    [SkippableFact]
    public async Task Duplicate_delivery_of_the_same_event_is_a_noop()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var carol = await RegisterAndLogin(client, "msg-carol");
        var categoryId = await CreateCategory(client, carol, "msg-dupe-cat");
        var threadId = await CreateThread(client, carol, categoryId, "Twice-deleted thread");
        (await PutLike(client, carol, "thread", threadId)).Count.ShouldBe(1);

        // Fabricate one ThreadDeleted event and deliver it twice — as broker redelivery or a relay retry would.
        var integrationEvent = new ThreadDeletedIntegrationEvent(
            Ulid.NewUlid(), Ulid.Parse(threadId, CultureInfo.InvariantCulture),
            Ulid.Parse(categoryId, CultureInfo.InvariantCulture), DateTimeOffset.UtcNow);
        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, IntegrationEventJson.SerializerOptions);
        await PublishRawAsync("content", "ThreadDeletedIntegrationEvent", body);
        await PublishRawAsync("content", "ThreadDeletedIntegrationEvent", body);

        await TestWait.UntilAsync(
            async () => (await GetSummary(client, "thread", threadId)).Count == 0,
            "the first delivery removes the reaction");
        await TestWait.UntilAsync(
            async () => await QueueCountAsync("engagement.events") == 0,
            "both deliveries drain from the work queue");
        await Task.Delay(500); // let the in-flight (second) delivery finish before asserting

        // The duplicate hit the inbox primary key and was skipped: one inbox row, nothing failed or poisoned.
        (await InboxCountAsync("forum_engagement", integrationEvent.EventId.ToString())).ShouldBe(1);
        (await InboxCountAsync("forum_files", integrationEvent.EventId.ToString())).ShouldBe(1);
        (await QueueCountAsync("engagement.events.poison")).ShouldBe(0u);
        (await QueueCountAsync("files.events.poison")).ShouldBe(0u);
        (await GetSummary(client, "thread", threadId)).Count.ShouldBe(0);
    }

    [SkippableFact]
    public async Task Malformed_message_is_parked_in_the_poison_queue_and_does_not_block_the_flow()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();

        // Garbage that fails deserialization is parked immediately in every consuming module's poison queue.
        await PublishRawAsync("content", "ThreadDeletedIntegrationEvent", "this is not json"u8.ToArray());
        await TestWait.UntilAsync(
            async () => await QueueCountAsync("files.events.poison") >= 1
                        && await QueueCountAsync("engagement.events.poison") >= 1,
            "the malformed message lands in both consuming modules' poison queues");

        // The queues keep working: a real deletion behind the poison message still flows end to end.
        var dave = await RegisterAndLogin(client, "msg-dave");
        var categoryId = await CreateCategory(client, dave, "msg-poison-cat");
        var threadId = await CreateThread(client, dave, categoryId, "Life goes on");
        (await PutLike(client, dave, "thread", threadId)).Count.ShouldBe(1);
        (await Send(client, dave, HttpMethod.Delete, $"/api/content/threads/{threadId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await TestWait.UntilAsync(
            async () => (await GetSummary(client, "thread", threadId)).Count == 0,
            "the queue is not blocked by the poison message");
    }

    // ---- broker + database probes ----

    private async Task PublishRawAsync(string exchange, string routingKey, byte[] body)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_factory.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();

        // Idempotent, same parameters as the hosts use — robust even if this runs before a consumer connected.
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);

        var properties = new BasicProperties { ContentType = "application/json", Persistent = true };
        await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, properties, body);
    }

    private async Task<uint> QueueCountAsync(string queue)
    {
        var factory = new ConnectionFactory { Uri = new Uri(_factory.RabbitMqConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        return await channel.MessageCountAsync(queue);
    }

    private async Task<OutboxRow?> FindThreadDeletedOutboxRowAsync(string threadId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetServices<DbContext>().First();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, processed_on_utc IS NOT NULL, correlation_id FROM forum_content.outbox_messages " +
            "WHERE type = @type AND payload->>'ThreadId' = @threadId";
        AddParameter(command, "@type", ThreadDeletedType);
        AddParameter(command, "@threadId", threadId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new OutboxRow(
            reader.GetString(0), reader.GetBoolean(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }

    private async Task<long> InboxCountAsync(string schema, string eventId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetServices<DbContext>().First();
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT count(*) FROM {schema}.inbox_messages WHERE id = @id";
        AddParameter(command, "@id", eventId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record OutboxRow(string EventId, bool Processed, string? CorrelationId);

    // ---- flow helpers (same shape as ContentFlowTests/EngagementFlowTests) ----

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

    private static async Task<SummaryPayload> PutLike(
        HttpClient client, Session session, string targetType, string targetId)
    {
        var response = await Send(client, session, HttpMethod.Put,
            $"/api/engagement/reactions/{targetType}/{targetId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SummaryPayload>())!;
    }

    private static async Task<SummaryPayload> GetSummary(HttpClient client, string targetType, string targetId) =>
        (await client.GetFromJsonAsync<SummaryPayload>($"/api/engagement/reactions/{targetType}/{targetId}"))!;

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

    private sealed record CategoryCreated(string CategoryId, string Slug);

    private sealed record ThreadCreated(string ThreadId);

    private sealed record SummaryPayload(string TargetId, int Count, bool ViewerReacted);
}
