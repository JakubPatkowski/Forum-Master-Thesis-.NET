using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Forum.Common.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// End-to-end Engagement flows against the real host + Postgres: idempotent like/unlike with trigger-maintained
/// counters, batch summaries, the private-category/permission gate, deletion cascades driven through the
/// registered consumers (as the Phase 6 bus will), and the user_stats_v karma view. Skipped when Docker is
/// unavailable.
/// </summary>
public sealed class EngagementFlowTests : IClassFixture<ForumApiFactory>
{
    private readonly ForumApiFactory _factory;

    public EngagementFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Like_toggle_is_idempotent_and_counts_stay_correct()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "eng-alice");
        var bob = await RegisterAndLogin(client, "eng-bob");
        var categoryId = await CreateCategory(client, alice, "eng-toggle-cat");
        var threadId = await CreateThread(client, alice, categoryId, "Likeable thread");

        // First like: count 1, viewer state true.
        var liked = await PutLike(client, alice, "thread", threadId);
        liked.Count.ShouldBe(1);
        liked.ViewerReacted.ShouldBeTrue();

        // Re-like: no-op, still 1 (no double count).
        (await PutLike(client, alice, "thread", threadId)).Count.ShouldBe(1);

        // A second user takes it to 2.
        (await PutLike(client, bob, "thread", threadId)).Count.ShouldBe(2);

        // Anonymous read: count without viewer state.
        var anonymous = (await client.GetFromJsonAsync<SummaryPayload>(
            $"/api/engagement/reactions/thread/{threadId}"))!;
        anonymous.Count.ShouldBe(2);
        anonymous.ViewerReacted.ShouldBeFalse();

        // Unlike: back to 1; unlike again: idempotent no-op.
        var unliked = await DeleteLike(client, alice, "thread", threadId);
        unliked.Count.ShouldBe(1);
        unliked.ViewerReacted.ShouldBeFalse();
        (await DeleteLike(client, alice, "thread", threadId)).Count.ShouldBe(1);
    }

    [SkippableFact]
    public async Task Batch_summaries_return_counts_and_viewer_state_per_target()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var carol = await RegisterAndLogin(client, "eng-carol");
        var dave = await RegisterAndLogin(client, "eng-dave");
        var categoryId = await CreateCategory(client, carol, "eng-batch-cat");
        var likedThread = await CreateThread(client, carol, categoryId, "Thread dave likes");
        var plainThread = await CreateThread(client, carol, categoryId, "Thread nobody likes");

        await PutLike(client, carol, "thread", likedThread);
        await PutLike(client, dave, "thread", likedThread);

        var response = await Send(client, dave, HttpMethod.Get,
            $"/api/engagement/reactions/batch?targetType=thread&targetIds={likedThread},{plainThread}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summaries = (await response.Content.ReadFromJsonAsync<List<SummaryPayload>>())!;

        summaries.Count.ShouldBe(2);
        var liked = summaries.Single(summary => summary.TargetId == likedThread);
        liked.Count.ShouldBe(2);
        liked.ViewerReacted.ShouldBeTrue();
        var plain = summaries.Single(summary => summary.TargetId == plainThread);
        plain.Count.ShouldBe(0);
        plain.ViewerReacted.ShouldBeFalse();

        // Guardrails: bad type and oversized lists are 422s, not scans.
        (await client.GetAsync($"/api/engagement/reactions/batch?targetType=category&targetIds={likedThread}"))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var tooMany = string.Join(',', Enumerable.Range(0, 101).Select(static _ => Ulid.NewUlid().ToString()));
        (await client.GetAsync($"/api/engagement/reactions/batch?targetType=thread&targetIds={tooMany}"))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [SkippableFact]
    public async Task A_private_category_gates_likes_to_its_owner_and_moderators()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var owner = await RegisterAndLogin(client, "eng-owner");
        var intruder = await RegisterAndLogin(client, "eng-intruder");
        var categoryId = await CreateCategory(client, owner, "eng-private-cat", "private");
        var threadId = await CreateThread(client, owner, categoryId, "Private thread");

        var forbidden = await Send(client, intruder, HttpMethod.Put,
            $"/api/engagement/reactions/thread/{threadId}");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The category owner may like their own private content.
        (await PutLike(client, owner, "thread", threadId)).Count.ShouldBe(1);
    }

    [SkippableFact]
    public async Task Liking_a_nonexistent_target_is_not_found()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var erin = await RegisterAndLogin(client, "eng-erin");

        var response = await Send(client, erin, HttpMethod.Put,
            $"/api/engagement/reactions/thread/{Ulid.NewUlid()}");
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Send(client, erin, HttpMethod.Put, "/api/engagement/reactions/banana/whatever"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Deleting_content_cascades_its_reactions_and_counters()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var frank = await RegisterAndLogin(client, "eng-frank");
        var grace = await RegisterAndLogin(client, "eng-grace");
        var categoryId = await CreateCategory(client, frank, "eng-cascade-cat");
        var threadId = await CreateThread(client, frank, categoryId, "Doomed thread");
        var commentId = await CreateComment(client, grace, threadId, "Doomed comment");

        (await PutLike(client, grace, "thread", threadId)).Count.ShouldBe(1);
        (await PutLike(client, frank, "comment", commentId)).Count.ShouldBe(1);

        // Comment goes first: its reactions vanish, the thread's stay.
        (await Send(client, grace, HttpMethod.Delete, $"/api/content/comments/{commentId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DispatchToAllConsumers(new CommentDeletedIntegrationEvent(
            Ulid.NewUlid(), Ulid.Parse(commentId, CultureInfo.InvariantCulture),
            Ulid.Parse(threadId, CultureInfo.InvariantCulture), DateTimeOffset.UtcNow));

        (await GetSummary(client, "comment", commentId)).Count.ShouldBe(0);
        (await GetSummary(client, "thread", threadId)).Count.ShouldBe(1);

        // Then the thread.
        (await Send(client, frank, HttpMethod.Delete, $"/api/content/threads/{threadId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await DispatchToAllConsumers(new ThreadDeletedIntegrationEvent(
            Ulid.NewUlid(), Ulid.Parse(threadId, CultureInfo.InvariantCulture), DateTimeOffset.UtcNow));

        (await GetSummary(client, "thread", threadId)).Count.ShouldBe(0);

        // The rows are gone, not just the counters: grace's viewer state is false even when authenticated.
        var summary = await Send(client, grace, HttpMethod.Get, $"/api/engagement/reactions/thread/{threadId}");
        (await summary.Content.ReadFromJsonAsync<SummaryPayload>())!.ViewerReacted.ShouldBeFalse();
    }

    [SkippableFact]
    public async Task User_stats_report_content_counts_and_karma()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var heidi = await RegisterAndLogin(client, "eng-heidi");
        var ivan = await RegisterAndLogin(client, "eng-ivan");
        var categoryId = await CreateCategory(client, heidi, "eng-stats-cat");
        var threadId = await CreateThread(client, heidi, categoryId, "Heidi's thread");
        var commentId = await CreateComment(client, heidi, threadId, "Heidi's comment");

        await PutLike(client, ivan, "thread", threadId);
        await PutLike(client, ivan, "comment", commentId);

        var heidiStats = (await client.GetFromJsonAsync<StatsPayload>(
            $"/api/engagement/users/{heidi.UserId}/stats"))!;
        heidiStats.Username.ShouldBe("eng-heidi");
        heidiStats.ThreadCount.ShouldBe(1);
        heidiStats.CommentCount.ShouldBe(1);
        heidiStats.Karma.ShouldBe(2);

        // Ivan gave likes but received none: zero across the board.
        var ivanStats = (await client.GetFromJsonAsync<StatsPayload>(
            $"/api/engagement/users/{ivan.UserId}/stats"))!;
        ivanStats.ThreadCount.ShouldBe(0);
        ivanStats.Karma.ShouldBe(0);

        (await client.GetAsync($"/api/engagement/users/{Ulid.NewUlid()}/stats"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task DispatchToAllConsumers<TEvent>(TEvent integrationEvent)
        where TEvent : class, IIntegrationEvent
    {
        // The relay lands in Phase 6 — drive ALL registered consumers, exactly as the bus does.
        using var scope = _factory.Services.CreateScope();
        foreach (var handler in scope.ServiceProvider.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(integrationEvent, CancellationToken.None);
        }
    }

    // ---- flow helpers (same shape as ContentFlowTests/FilesFlowTests) ----

    private static async Task<SummaryPayload> PutLike(
        HttpClient client, Session session, string targetType, string targetId)
    {
        var response = await Send(client, session, HttpMethod.Put,
            $"/api/engagement/reactions/{targetType}/{targetId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SummaryPayload>())!;
    }

    private static async Task<SummaryPayload> DeleteLike(
        HttpClient client, Session session, string targetType, string targetId)
    {
        var response = await Send(client, session, HttpMethod.Delete,
            $"/api/engagement/reactions/{targetType}/{targetId}");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SummaryPayload>())!;
    }

    private static async Task<SummaryPayload> GetSummary(HttpClient client, string targetType, string targetId) =>
        (await client.GetFromJsonAsync<SummaryPayload>($"/api/engagement/reactions/{targetType}/{targetId}"))!;

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

    private sealed record CommentCreated(string CommentId);

    private sealed record SummaryPayload(string TargetId, int Count, bool ViewerReacted);

    private sealed record StatsPayload(
        string UserId, string Username, string DisplayName, int ThreadCount, int CommentCount, int Karma);
}
