using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// The Content read cache (docs/specs/READ-CACHE-CONTRACT.md) switched ON against the real host + Postgres: a write
/// is visible on the next read of the same instance (§3 invalidation), the read gate runs before every cached read
/// (a private category's cached feed/search never reaches an anonymous reader), and the lookups land on
/// <c>/metrics</c> as hit/miss. Own factory: <c>ReadCache:Enabled=true</c>. Skipped when Docker is unavailable.
/// </summary>
public sealed class ReadCacheFlowTests : IClassFixture<ReadCacheFlowTests.ReadCacheEnabledFactory>
{
    private const string ModeratorRoleId = "00000000000000000000000002";

    private readonly ReadCacheEnabledFactory _factory;

    public ReadCacheFlowTests(ReadCacheEnabledFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Feed_comment_tree_and_search_show_a_write_right_after_it_on_the_same_instance()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "rc-alice");
        var categoryId = await CreateCategory(client, alice, "rc-feed-cat");
        var word = $"zymurgy{Ulid.NewUlid().ToString()[^6..].ToLowerInvariant()}";
        var first = await CreateThread(client, alice, categoryId, $"First {word}");

        // Warm every cache entry (miss), read again (hit).
        (await GetFeed(client, categoryId)).Items.Select(static item => item.Id).ShouldBe([first]);
        (await GetFeed(client, categoryId)).Items.Count.ShouldBe(1);
        (await GetComments(client, first)).Count.ShouldBe(0);
        (await Search(client, word, "newest")).Items.Select(static item => item.Id).ShouldBe([first]);

        // Thread create → C1, C2, C3 flushed: the very next reads see it (no TTL wait).
        var second = await CreateThread(client, alice, categoryId, $"Second {word}");
        (await GetFeed(client, categoryId)).Items.Select(static item => item.Id).ShouldBe([second, first]);
        (await Search(client, word, "newest")).Items.Select(static item => item.Id).ShouldBe([second, first]);
        (await Search(client, word, "relevance")).Items.Select(static item => item.Id).ShouldContain(second);

        // Comment create → C2 of that thread: the tree shows the comment at once.
        var commentId = await CreateComment(client, alice, first, "hello");
        (await GetComments(client, first)).Select(static comment => comment.Id).ShouldBe([commentId]);

        // Comment delete → C1 + C2 flushed: the tombstone is visible immediately.
        (await Send(client, alice, HttpMethod.Delete, $"/api/content/comments/{commentId}")).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);
        (await GetComments(client, first)).Single().Body.ShouldBe("[deleted]");

        // Pin → everything flushed: the pinned thread moves to the top of the feed at once.
        await GrantRole(alice.UserId, ModeratorRoleId);
        (await Send(client, alice, HttpMethod.Post, $"/api/content/threads/{first}/pin", new { pinned = true }))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetFeed(client, categoryId)).Items.Select(static item => item.Id).ShouldBe([first, second]);

        var metrics = await client.GetStringAsync("/metrics");
        metrics.ShouldContain("forum_read_cache_lookups_total");
        metrics.ShouldContain("cache=\"feed\",result=\"hit\"");
        metrics.ShouldContain("cache=\"feed\",result=\"miss\"");
        metrics.ShouldContain("cache=\"comments\",result=\"miss\"");
        metrics.ShouldContain("cache=\"search\",result=\"miss\"");
        metrics.ShouldNotContain("result=\"bypass\"");
    }

    [SkippableFact]
    public async Task A_moderators_cached_private_feed_and_search_never_reach_an_anonymous_or_outside_reader()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var owner = await RegisterAndLogin(client, "rc-owner");
        var outsider = await RegisterAndLogin(client, "rc-outsider");
        var moderator = await RegisterAndLogin(client, "rc-moderator");
        await GrantRole(moderator.UserId, ModeratorRoleId);

        var privateId = await CreateCategory(client, owner, "rc-private-cat", "private");
        var word = $"quokka{Ulid.NewUlid().ToString()[^6..].ToLowerInvariant()}";
        var secret = await CreateThread(client, owner, privateId, $"Members only {word}");
        await CreateComment(client, owner, secret, "private comment");

        // The moderator warms every entry: feed, tree and a search page that contains the private thread.
        (await GetFeed(client, privateId, moderator)).Items.Select(static item => item.Id).ShouldBe([secret]);
        (await GetComments(client, secret, moderator)).Count.ShouldBe(1);
        (await Search(client, word, "newest", moderator)).Items.Select(static item => item.Id).ShouldBe([secret]);
        (await Search(client, word, "relevance", moderator)).Items.Count.ShouldBe(1);

        // Gate before cache: the warmed feed/tree answer 404 to everyone else, and search scopes exclude the thread.
        (await Get(client, null, $"/api/content/threads?categoryId={privateId}&limit=20")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Get(client, outsider, $"/api/content/threads?categoryId={privateId}&limit=20")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Get(client, null, $"/api/content/threads/{secret}/comments")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Get(client, outsider, $"/api/content/threads/{secret}/comments")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Search(client, word, "newest")).Items.ShouldBeEmpty();
        (await Search(client, word, "relevance")).Items.ShouldBeEmpty();
        (await Search(client, word, "newest", outsider)).Items.ShouldBeEmpty();

        // The moderator's own entries are still hits for the moderator.
        (await GetFeed(client, privateId, moderator)).Items.Count.ShouldBe(1);
        (await Search(client, word, "newest", moderator)).Items.Count.ShouldBe(1);

        // Access revoked: the ex-moderator's next search is a different scope (a miss), with no private rows.
        await RevokeRole(moderator.UserId, ModeratorRoleId);
        (await Search(client, word, "newest", moderator)).Items.ShouldBeEmpty();
        (await Get(client, moderator, $"/api/content/threads?categoryId={privateId}&limit=20")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---- helpers ----

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

    private static async Task<string> CreateCategory(HttpClient client, Session session, string slug, string visibility = "public")
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/content/categories",
            new { slug, name = $"Category {slug}", description = "test", visibility });
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

    private static async Task<string> CreateComment(HttpClient client, Session session, string threadId, string body)
    {
        var response = await Send(client, session, HttpMethod.Post, $"/api/content/threads/{threadId}/comments",
            new { parentId = (string?)null, body });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CommentCreated>())!.CommentId;
    }

    private static async Task<FeedPage> GetFeed(HttpClient client, string categoryId, Session? session = null)
    {
        var response = await Get(client, session, $"/api/content/threads?categoryId={categoryId}&limit=20");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FeedPage>())!;
    }

    private static async Task<List<CommentNode>> GetComments(HttpClient client, string threadId, Session? session = null)
    {
        var response = await Get(client, session, $"/api/content/threads/{threadId}/comments");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<CommentNode>>())!;
    }

    private static async Task<FeedPage> Search(HttpClient client, string q, string sort, Session? session = null)
    {
        var response = await Get(client, session, $"/api/content/search?q={Uri.EscapeDataString(q)}&sort={sort}&limit=20");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FeedPage>())!;
    }

    private static Task<HttpResponseMessage> Get(HttpClient client, Session? session, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (session is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        return client.SendAsync(request);
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

    // Role rows only (no effective_perm_cache row exists for these users, so has_permission resolves live) — the
    // same helpers RealtimeFlowTests uses.
    private Task GrantRole(string userId, string roleId) => ExecuteSqlAsync(
        "INSERT INTO forum_authz.user_roles (user_id, role_id) VALUES ({0}, {1}) ON CONFLICT DO NOTHING", userId, roleId);

    private Task RevokeRole(string userId, string roleId) => ExecuteSqlAsync(
        "DELETE FROM forum_authz.user_roles WHERE user_id = {0} AND role_id = {1}", userId, roleId);

    private async Task ExecuteSqlAsync(string sql, params object[] parameters)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetServices<DbContext>().First();
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    public sealed class ReadCacheEnabledFactory : ForumApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ReadCache:Enabled", "true");
        }
    }

    private sealed record Session(string UserId, string AccessToken);

    private sealed record RegisterResponse(string UserId);

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresOnUtc);

    private sealed record CategoryCreated(string CategoryId, string Slug);

    private sealed record ThreadCreated(string ThreadId);

    private sealed record CommentCreated(string CommentId);

    private sealed record FeedItem(string Id, string Title, bool IsPinned);

    private sealed record FeedPage(List<FeedItem> Items, string? NextCursor, bool HasMore);

    private sealed record CommentNode(string Id, string Body, int Depth);
}
