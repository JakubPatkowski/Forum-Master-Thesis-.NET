using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>End-to-end identity flows against the real host + Postgres: register → login → refresh, reuse detection, and authz gating.</summary>
public sealed class IdentityFlowTests : IClassFixture<ForumApiFactory>
{
    private const string AdminRoleId = "00000000000000000000000003";

    private readonly ForumApiFactory _factory;

    public IdentityFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Register_login_refresh_rotates_and_detects_reuse()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        const string email = "alice@example.com";
        const string password = "Password123!";

        var register = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { username = "alice", email, displayName = "Alice", password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/identity/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken.ShouldNotBeNullOrWhiteSpace();
        var firstRefresh = ExtractRefreshToken(login);
        firstRefresh.ShouldNotBeNull();

        // Rotate: the first token works exactly once and yields a new token.
        var rotate = await Refresh(client, firstRefresh!);
        rotate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondRefresh = ExtractRefreshToken(rotate);
        secondRefresh.ShouldNotBeNull();
        secondRefresh.ShouldNotBe(firstRefresh);

        // Reuse the now-rotated first token: theft detection rejects it and revokes the family.
        var reuse = await Refresh(client, firstRefresh!);
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The whole family is revoked, so the legitimately-rotated token no longer works either.
        var afterReuse = await Refresh(client, secondRefresh!);
        afterReuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Login_does_not_reveal_whether_an_account_exists()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();

        var unknown = await client.PostAsJsonAsync(
            "/api/identity/login", new { email = "ghost@example.com", password = "whatever123" });

        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Admin_endpoint_is_403_without_permission_and_200_with_it()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        const string email = "bob@example.com";
        const string password = "Password123!";

        var register = await client.PostAsJsonAsync(
            "/api/identity/register",
            new { username = "bob", email, displayName = "Bob", password });
        var userId = (await register.Content.ReadFromJsonAsync<RegisterResponse>())!.UserId;

        var login = await client.PostAsJsonAsync("/api/identity/login", new { email, password });
        var accessToken = (await login.Content.ReadFromJsonAsync<TokenResponse>())!.AccessToken;

        // Anonymous → 401.
        (await client.GetAsync("/api/identity/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Authenticated but only a 'user' → 403.
        using (var forbidden = new HttpRequestMessage(HttpMethod.Get, "/api/identity/admin/users"))
        {
            forbidden.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            (await client.SendAsync(forbidden)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Grant the admin role (the SQL ACL is the live source of truth — same token now resolves 'manage').
        await GrantRole(userId, AdminRoleId);
        using (var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/identity/admin/users"))
        {
            allowed.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            (await client.SendAsync(allowed)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    private static Task<HttpResponseMessage> Refresh(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/refresh");
        request.Headers.Add("Cookie", $"refresh_token={refreshToken}");
        return client.SendAsync(request);
    }

    private static string? ExtractRefreshToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        const string prefix = "refresh_token=";
        var cookie = cookies.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
        if (cookie is null)
        {
            return null;
        }

        var value = cookie[prefix.Length..];
        var semicolon = value.IndexOf(';', StringComparison.Ordinal);
        return semicolon >= 0 ? value[..semicolon] : value;
    }

    private async Task GrantRole(string userId, string roleId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetServices<DbContext>().First();
        await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO forum_authz.user_roles (user_id, role_id) VALUES ({0}, {1}) ON CONFLICT DO NOTHING",
            userId, roleId);
    }

    private sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresOnUtc);

    private sealed record RegisterResponse(string UserId);
}
