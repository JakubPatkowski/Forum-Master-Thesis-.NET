using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Forum.Common.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Files.Application.Sweep;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// End-to-end Files flows against the real host + Postgres + MinIO: presigned upload (bytes never touch the
/// API), commit verification of the REAL type/size, attach gated by Content's rules, presigned download,
/// deletion-event detach and the orphan sweep. Skipped when Docker is unavailable.
/// </summary>
public sealed class FilesFlowTests : IClassFixture<ForumApiFactory>
{
    private readonly ForumApiFactory _factory;

    public FilesFlowTests(ForumApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    [SkippableFact]
    public async Task Presigned_upload_commit_attach_download_and_detach_flow()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var alice = await RegisterAndLogin(client, "files-alice");
        var categoryId = await CreateCategory(client, alice, "files-cat");
        var threadId = await CreateThread(client, alice, categoryId, "Thread with a picture");

        // Initiate: a pending row + a presigned PUT pointing straight at MinIO.
        var png = PngBytes(640, 480);
        var initiated = await InitiateUpload(client, alice, "image/png", png.Length);
        initiated.Method.ShouldBe("PUT");
        initiated.UploadUrl.ShouldContain(initiated.ObjectKey);

        // Upload: the bytes go to the presigned URL, not to the API.
        (await UploadBytes(initiated.UploadUrl, png, "image/png")).EnsureSuccessStatusCode();

        // Commit: the backend stats + sniffs the stored object and records the decoded dimensions.
        var commit = await Send(client, alice, HttpMethod.Post, $"/api/files/{initiated.FileId}/commit");
        commit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var committed = (await commit.Content.ReadFromJsonAsync<CommitPayload>())!;
        committed.Width.ShouldBe(640);
        committed.Height.ShouldBe(480);

        // Attach to the thread (alice owns it) and read it back anonymously.
        var attach = await Send(client, alice, HttpMethod.Post, $"/api/files/{initiated.FileId}/attachments",
            new { targetType = "thread", targetId = threadId });
        attach.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var listed = await ListFiles(client, "thread", threadId);
        var item = listed.ShouldHaveSingleItem();
        item.FileId.ShouldBe(initiated.FileId);
        item.ContentType.ShouldBe("image/png");

        // The presigned GET serves the exact bytes without the API in the path.
        using (var http = new HttpClient())
        {
            var downloaded = await http.GetByteArrayAsync(new Uri(item.Url));
            downloaded.ShouldBe(png);
        }

        // Detach empties the target's list again.
        var detach = await Send(client, alice, HttpMethod.Delete,
            $"/api/files/{initiated.FileId}/attachments?targetType=thread&targetId={threadId}");
        detach.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ListFiles(client, "thread", threadId)).ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task Commit_rejects_a_size_mismatch_against_the_declared_value()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var bob = await RegisterAndLogin(client, "files-bob");

        var png = PngBytes(10, 10);
        var initiated = await InitiateUpload(client, bob, "image/png", png.Length + 5); // lies about the size
        (await UploadBytes(initiated.UploadUrl, png, "image/png")).EnsureSuccessStatusCode();

        var commit = await Send(client, bob, HttpMethod.Post, $"/api/files/{initiated.FileId}/commit");

        commit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemCode(commit)).ShouldBe("file.size_mismatch");
    }

    [SkippableFact]
    public async Task Commit_rejects_bytes_whose_real_type_differs_from_the_declared_type()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var carol = await RegisterAndLogin(client, "files-carol");

        var gif = GifBytes(10, 10);
        var initiated = await InitiateUpload(client, carol, "image/png", gif.Length); // declares png, uploads gif
        (await UploadBytes(initiated.UploadUrl, gif, "image/png")).EnsureSuccessStatusCode();

        var commit = await Send(client, carol, HttpMethod.Post, $"/api/files/{initiated.FileId}/commit");

        commit.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await ReadProblemCode(commit)).ShouldBe("file.type_mismatch");
    }

    [SkippableFact]
    public async Task Committing_before_uploading_conflicts()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var dave = await RegisterAndLogin(client, "files-dave");

        var initiated = await InitiateUpload(client, dave, "image/png", 100);
        var commit = await Send(client, dave, HttpMethod.Post, $"/api/files/{initiated.FileId}/commit");

        commit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadProblemCode(commit)).ShouldBe("file.not_uploaded");
    }

    [SkippableFact]
    public async Task Attaching_to_someone_elses_thread_is_forbidden()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var author = await RegisterAndLogin(client, "files-author");
        var intruder = await RegisterAndLogin(client, "files-intruder");
        var categoryId = await CreateCategory(client, author, "files-authz-cat");
        var threadId = await CreateThread(client, author, categoryId, "Someone else's thread");

        var fileId = await UploadAndCommit(client, intruder, PngBytes(10, 10));
        var attach = await Send(client, intruder, HttpMethod.Post, $"/api/files/{fileId}/attachments",
            new { targetType = "thread", targetId = threadId });

        attach.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [SkippableFact]
    public async Task Deleting_a_thread_detaches_its_files_and_the_sweep_collects_the_orphan()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var erin = await RegisterAndLogin(client, "files-erin");
        var categoryId = await CreateCategory(client, erin, "files-sweep-cat");
        var threadId = await CreateThread(client, erin, categoryId, "Doomed thread");

        var fileId = await UploadAndCommit(client, erin, PngBytes(20, 20));
        (await Send(client, erin, HttpMethod.Post, $"/api/files/{fileId}/attachments",
            new { targetType = "thread", targetId = threadId })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Send(client, erin, HttpMethod.Delete, $"/api/content/threads/{threadId}"))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The relay lands in Phase 6 — drive the registered consumer directly, exactly as the bus will.
        using (var scope = _factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetRequiredService<IIntegrationEventHandler<ThreadDeletedIntegrationEvent>>();
            await handler.HandleAsync(
                new ThreadDeletedIntegrationEvent(
                    Ulid.NewUlid(), Ulid.Parse(threadId, CultureInfo.InvariantCulture), DateTimeOffset.UtcNow),
                CancellationToken.None);
        }

        (await ListFiles(client, "thread", threadId)).ShouldBeEmpty();

        // The unattached committed file is past its (zeroed) grace window — the sweep removes blob + row.
        var result = await RunSweep();
        result.UnattachedSwept.ShouldBeGreaterThanOrEqualTo(1);
        (await client.GetAsync($"/api/files/{fileId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task An_abandoned_pending_upload_is_swept()
    {
        Skip.IfNot(_factory.Available, "Docker is not available.");

        var client = CreateClient();
        var frank = await RegisterAndLogin(client, "files-frank");

        var initiated = await InitiateUpload(client, frank, "image/png", 100); // never uploads, never commits

        var result = await RunSweep();
        result.Acquired.ShouldBeTrue();
        result.PendingSwept.ShouldBeGreaterThanOrEqualTo(1);

        // The row is gone: even the uploader can no longer commit it.
        var commit = await Send(client, frank, HttpMethod.Post, $"/api/files/{initiated.FileId}/commit");
        commit.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<SweepResult> RunSweep()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OrphanSweeper>().SweepAsync(CancellationToken.None);
    }

    // ---- image bytes (header-valid is enough: commit only stats + probes, it never decodes pixels) ----

    private static byte[] PngBytes(int width, int height)
    {
        var bytes = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.ToArray().CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;
        bytes[25] = 6;
        return bytes;
    }

    private static byte[] GifBytes(int width, int height)
    {
        var bytes = new byte[32];
        "GIF89a"u8.ToArray().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)height);
        return bytes;
    }

    // ---- flow helpers ----

    private static async Task<string> UploadAndCommit(HttpClient client, Session session, byte[] png)
    {
        var initiated = await InitiateUpload(client, session, "image/png", png.Length);
        (await UploadBytes(initiated.UploadUrl, png, "image/png")).EnsureSuccessStatusCode();
        var commit = await Send(client, session, HttpMethod.Post, $"/api/files/{initiated.FileId}/commit");
        commit.StatusCode.ShouldBe(HttpStatusCode.OK);
        return initiated.FileId;
    }

    private static async Task<InitiatePayload> InitiateUpload(
        HttpClient client, Session session, string contentType, long sizeBytes)
    {
        var response = await Send(client, session, HttpMethod.Post, "/api/files", new { contentType, sizeBytes });
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InitiatePayload>())!;
    }

    private static async Task<HttpResponseMessage> UploadBytes(string presignedUrl, byte[] bytes, string contentType)
    {
        using var http = new HttpClient();
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return await http.PutAsync(new Uri(presignedUrl), content);
    }

    private static async Task<List<FilePayload>> ListFiles(HttpClient client, string targetType, string targetId) =>
        (await client.GetFromJsonAsync<List<FilePayload>>(
            $"/api/files?targetType={targetType}&targetId={targetId}"))!;

    private static async Task<string?> ReadProblemCode(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // ---- identity/content helpers (same shape as ContentFlowTests) ----

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

    private sealed record InitiatePayload(
        string FileId, string ObjectKey, string UploadUrl, string Method, DateTimeOffset ExpiresOnUtc);

    private sealed record CommitPayload(
        string FileId, string ContentType, long SizeBytes, int? Width, int? Height);

    private sealed record FilePayload(
        string FileId, string Url, string ContentType, long SizeBytes, int? Width, int? Height,
        DateTimeOffset ExpiresOnUtc);
}
