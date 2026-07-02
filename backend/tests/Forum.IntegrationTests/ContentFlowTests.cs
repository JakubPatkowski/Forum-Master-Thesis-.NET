using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// End-to-end Content flows against the real host + Postgres: categories → threads → nested comments,
/// keyset feed paging, FTS search, and ownership-vs-moderator authorization. Skipped when Docker is unavailable.
/// </summary>
public sealed class ContentFlowTests : IClassFixture<ForumApiFactory>
{
    private const string ModeratorRoleId = "00000000000000000000000002";

    private readonly ForumApiFactory _factory;

    public ContentFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Thread_feed_pages_by_keyset_without_duplicates()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "feed-alice");
        var categoryId = await CreateCategory(client, alice, "feed-cat");

        var created = new List<string>();
        for (var i = 1; i <= 5; i++)
        {
            created.Add(await CreateThread(client, alice, categoryId, $"Feed thread {i}", tagSlugs: ["keyset"]));
        }

        // Page 1 + 2: two items each, with a cursor; page 3: the last item, no more.
        var page1 = await GetFeed(client, categoryId, cursor: null, limit: 2);
        page1.Items.Count.ShouldBe(2);
        page1.HasMore.ShouldBeTrue();
        page1.NextCursor.ShouldNotBeNullOrWhiteSpace();

        var page2 = await GetFeed(client, categoryId, page1.NextCursor, limit: 2);
        page2.Items.Count.ShouldBe(2);
        page2.HasMore.ShouldBeTrue();

        var page3 = await GetFeed(client, categoryId, page2.NextCursor, limit: 2);
        page3.Items.Count.ShouldBe(1);
        page3.HasMore.ShouldBeFalse();
        page3.NextCursor.ShouldBeNull();

        var seen = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(static item => item.Id).ToArray();
        seen.Distinct().Count().ShouldBe(5);
        seen.ShouldBe(created.AsEnumerable().Reverse().ToArray()); // newest first

        // The detail view resolves the attached tags.
        var detail = await client.GetFromJsonAsync<ThreadDetail>($"/api/content/threads/{created[0]}");
        detail!.Tags.ShouldBe(["keyset"]);
    }

    [SkippableFact]
    public async Task Comments_nest_to_depth_five_and_the_sixth_level_is_rejected()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var bob = await RegisterAndLogin(client, "nest-bob");
        var categoryId = await CreateCategory(client, bob, "nest-cat");
        var threadId = await CreateThread(client, bob, categoryId, "Nesting thread");

        // Root (depth 0) + replies down to depth 5 — all accepted.
        var parentId = (string?)null;
        var chain = new List<string>();
        for (var depth = 0; depth <= 5; depth++)
        {
            var response = await Send(client, bob, HttpMethod.Post, $"/api/content/threads/{threadId}/comments",
                new { parentId, body = $"depth {depth}" });
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            parentId = (await response.Content.ReadFromJsonAsync<CommentCreated>())!.CommentId;
            chain.Add(parentId!);
        }

        // Depth 6 exceeds the cap → 422.
        var tooDeep = await Send(client, bob, HttpMethod.Post, $"/api/content/threads/{threadId}/comments",
            new { parentId, body = "depth 6" });
        tooDeep.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // The tree comes back in path (depth-first) order.
        var tree = (await client.GetFromJsonAsync<List<CommentNode>>($"/api/content/threads/{threadId}/comments"))!;
        tree.Select(static comment => comment.Id).ShouldBe(chain);
        tree.Select(static comment => comment.Depth).ShouldBe([0, 1, 2, 3, 4, 5]);
    }

    [SkippableFact]
    public async Task Deleting_a_comment_blanks_the_body_and_keeps_its_children()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var carol = await RegisterAndLogin(client, "del-carol");
        var categoryId = await CreateCategory(client, carol, "del-cat");
        var threadId = await CreateThread(client, carol, categoryId, "Deletion thread");

        var root = await CreateComment(client, carol, threadId, parentId: null, "root comment");
        var child = await CreateComment(client, carol, threadId, root, "child comment");

        var delete = await Send(client, carol, HttpMethod.Delete, $"/api/content/comments/{root}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var tree = await client.GetFromJsonAsync<List<CommentNode>>($"/api/content/threads/{threadId}/comments");
        tree!.Count.ShouldBe(2);

        var deletedRoot = tree.Single(comment => comment.Id == root);
        deletedRoot.IsDeleted.ShouldBeTrue();
        deletedRoot.Body.ShouldBe("[deleted]");

        var keptChild = tree.Single(comment => comment.Id == child);
        keptChild.IsDeleted.ShouldBeFalse();
        keptChild.Body.ShouldBe("child comment");
    }

    [SkippableFact]
    public async Task Full_text_search_finds_only_the_matching_thread()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var dave = await RegisterAndLogin(client, "fts-dave");
        var categoryId = await CreateCategory(client, dave, "fts-cat");

        var zander = await CreateThread(client, dave, categoryId, "Catching zander at dawn");
        await CreateThread(client, dave, categoryId, "Carp bait recipes");

        var results = await client.GetFromJsonAsync<FeedPage>("/api/content/search?q=zander");

        var hit = results!.Items.ShouldHaveSingleItem();
        hit.Id.ShouldBe(zander);
        hit.Title.ShouldBe("Catching zander at dawn");
    }

    [SkippableFact]
    public async Task Moderator_can_pin_and_delete_someone_elses_thread_but_a_plain_user_cannot()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var author = await RegisterAndLogin(client, "mod-author");
        var other = await RegisterAndLogin(client, "mod-other");

        var categoryId = await CreateCategory(client, author, "mod-cat");
        var older = await CreateThread(client, author, categoryId, "Older thread");
        var newer = await CreateThread(client, author, categoryId, "Newer thread");

        // A plain authenticated non-owner gets 403 (not 404, not 500).
        var forbidden = await Send(client, other, HttpMethod.Delete, $"/api/content/threads/{older}");
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Promote to moderator: the SQL ACL is the live source of truth, the same token now resolves 'moderate'.
        await GrantRole(other.UserId, ModeratorRoleId);

        var pin = await Send(client, other, HttpMethod.Post, $"/api/content/threads/{older}/pin", new { pinned = true });
        pin.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Pinned wins over newer in the keyset order, and the cursor crosses the pinned boundary cleanly.
        var page1 = await GetFeed(client, categoryId, cursor: null, limit: 1);
        page1.Items.ShouldHaveSingleItem().Id.ShouldBe(older);
        var page2 = await GetFeed(client, categoryId, page1.NextCursor, limit: 1);
        page2.Items.ShouldHaveSingleItem().Id.ShouldBe(newer);

        // The moderator deletes the author's thread; it disappears from the feed and 404s directly.
        var delete = await Send(client, other, HttpMethod.Delete, $"/api/content/threads/{newer}");
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var feed = await GetFeed(client, categoryId, cursor: null, limit: 10);
        feed.Items.Select(static item => item.Id).ShouldBe([older]);

        (await client.GetAsync($"/api/content/threads/{newer}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

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
        HttpClient client, Session session, string categoryId, string title, string[]? tagSlugs = null)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/content/threads",
            new { categoryId, title, body = $"Body of: {title}", tagSlugs });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ThreadCreated>())!.ThreadId;
    }

    private static async Task<string> CreateComment(
        HttpClient client, Session session, string threadId, string? parentId, string body)
    {
        var response = await Send(client, session, HttpMethod.Post, $"/api/content/threads/{threadId}/comments",
            new { parentId, body });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CommentCreated>())!.CommentId;
    }

    private static async Task<FeedPage> GetFeed(HttpClient client, string categoryId, string? cursor, int limit)
    {
        var url = $"/api/content/threads?categoryId={categoryId}&limit={limit}";
        if (cursor is not null)
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return (await client.GetFromJsonAsync<FeedPage>(url))!;
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

    private async Task GrantRole(string userId, string roleId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetServices<DbContext>().First();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO forum_authz.user_roles (user_id, role_id) VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
            userId, roleId);
    }

    private sealed record Session(string UserId, string AccessToken);

    private sealed record RegisterResponse(string UserId);

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresOnUtc);

    private sealed record CategoryCreated(string CategoryId, string Slug);

    private sealed record ThreadCreated(string ThreadId);

    private sealed record CommentCreated(string CommentId);

    private sealed record FeedPage(List<FeedItem> Items, string? NextCursor, bool HasMore);

    private sealed record FeedItem(string Id, string Title, bool IsPinned, string Username);

    private sealed record ThreadDetail(string Id, string Title, string Body, List<string> Tags);

    private sealed record CommentNode(
        string Id, string? ParentId, string Path, int Depth, string Body, bool IsDeleted);
}
