using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kestrel.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Kestrel.Tests.Ingest;

/// <summary>
/// Hosts the real API with isolated per-fixture SQLite storage and a small
/// upload cap, so the full ingest pipeline (endpoint → queue → processor →
/// store) runs end to end in tests.
/// </summary>
public sealed class IngestApiFactory : WebApplicationFactory<Program>
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "kestrel-tests", "api-" + Guid.NewGuid().ToString("N"));

    public IngestApiFactory()
    {
        Directory.CreateDirectory(_root);
    }

    private string DatabasePath => Path.Combine(_root, "kestrel.db");

    private string TempDirectory => Path.Combine(_root, "uploads-tmp");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={DatabasePath}");
        builder.UseSetting("ConnectionStrings:EventsConnection", $"Data Source={DatabasePath}");
        builder.UseSetting("KestrelApp:Ingest:MaxUploadBytes", "262144"); // 256 KiB — keeps tests fast
        builder.UseSetting("KestrelApp:Ingest:JobTimeoutMinutes", "1");
        builder.UseSetting("KestrelApp:Ingest:TempDirectory", TempDirectory);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class IngestEndpointTests : IClassFixture<IngestApiFactory>
{
    private static readonly byte[] Magic = "ElfFile\0"u8.ToArray();

    private readonly IngestApiFactory _factory;

    public IngestEndpointTests(IngestApiFactory factory) => _factory = factory;

    private HttpClient CreateClient() => _factory.CreateClient();

    private async Task<HttpClient> CreateLoggedInClientAsync(string username, string password)
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.True(response.IsSuccessStatusCode, $"login failed for {username}: {(int)response.StatusCode}");

        // TestServer clients have no cookie jar — attach the session cookie
        // pair (kestrel_auth=...) to every request of this client explicitly.
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out var cookieValues),
            "login did not set a session cookie");
        var setCookie = Assert.Single(cookieValues!);
        var pairEnd = setCookie.IndexOf(';');
        client.DefaultRequestHeaders.Add("Cookie", pairEnd < 0 ? setCookie : setCookie[..pairEnd]);
        return client;
    }

    private Task<HttpClient> CreateAdminClientAsync() =>
        CreateLoggedInClientAsync("admin", IdentityInitializationService.DevelopmentBootstrapPassword);

    private async Task<HttpClient> CreateViewerClientAsync()
    {
        var admin = await CreateAdminClientAsync();
        var username = "viewer_" + Guid.NewGuid().ToString("N")[..10];
        var create = await admin.PostAsJsonAsync("/api/users", new
        {
            username,
            password = "Viewer-Passw0rd!X",
            roles = new[] { "Viewer" },
        });
        Assert.True(
            create.IsSuccessStatusCode || create.StatusCode == HttpStatusCode.Conflict,
            $"viewer creation failed: {(int)create.StatusCode}");
        return await CreateLoggedInClientAsync(username, "Viewer-Passw0rd!X");
    }

    private static ByteArrayContent EvtxBody(params byte[][] chunks)
    {
        var bytes = new byte[chunks.Sum(chunk => chunk.Length)];
        var offset = 0;
        foreach (var chunk in chunks)
        {
            Buffer.BlockCopy(chunk, 0, bytes, offset, chunk.Length);
            offset += chunk.Length;
        }

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    [Fact]
    public async Task Upload_requires_authentication()
    {
        using var client = CreateClient();
        using var content = EvtxBody(Magic);

        var response = await client.PostAsync("/api/ingest/upload", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_is_forbidden_for_viewers()
    {
        using var viewer = await CreateViewerClientAsync();
        using var content = EvtxBody(Magic);

        var response = await viewer.PostAsync("/api/ingest/upload", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_with_wrong_magic_is_rejected_400()
    {
        using var admin = await CreateAdminClientAsync();
        using var content = EvtxBody(Encoding.ASCII.GetBytes("NotEvtx!"));

        var response = await admin.PostAsync("/api/ingest/upload", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_evtx", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Upload_above_cap_is_rejected_413()
    {
        using var admin = await CreateAdminClientAsync();
        var oversize = new byte[300_000]; // above the fixture's 256 KiB cap
        Magic.CopyTo(oversize.AsSpan());
        using var content = EvtxBody(oversize);

        var response = await admin.PostAsync("/api/ingest/upload", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("file_too_large", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Magic_valid_upload_is_accepted_then_fails_with_an_explicit_reason()
    {
        using var admin = await CreateAdminClientAsync();

        // Passes the magic-byte gate but is not a real EVTX container — the
        // background parser must land the job in a terminal state with a
        // reason (no silent failures).
        var junk = new byte[4096];
        Magic.CopyTo(junk.AsSpan());
        using var content = EvtxBody(junk, Encoding.ASCII.GetBytes(new string('x', 512)));
        content.Headers.Add("X-Evtx-Filename", "fake.evtx");

        var response = await admin.PostAsync("/api/ingest/upload", content);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var accepted = await response.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = accepted.GetProperty("jobId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(jobId));

        var job = await WaitForTerminalAsync(admin, jobId!);
        Assert.Equal("failed", job.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Job_list_is_available_to_viewers()
    {
        using var viewer = await CreateViewerClientAsync();

        var response = await viewer.GetAsync("/api/ingest/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jobs = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, jobs.ValueKind);
    }

    private static async Task<JsonElement> WaitForTerminalAsync(HttpClient client, string jobId)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var response = await client.GetAsync($"/api/ingest/jobs/{jobId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var job = await response.Content.ReadFromJsonAsync<JsonElement>();
            var status = job.GetProperty("status").GetString();
            if (status is "completed" or "completed_with_errors" or "failed" or "canceled")
            {
                return job;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Job {jobId} did not reach a terminal state within 15 seconds.");
    }
}