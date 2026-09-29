using System.Net;
using System.Net.Http.Json;
using Kestrel.Api.Auth;
using Kestrel.Tests.Support;
using Xunit;

namespace Kestrel.Tests.Auth;

/// <summary>
/// Phase 1 auth-gate contract: no endpoint serves data without an
/// authenticated session; cookie is hardened; failures are generic.
/// </summary>
public class AuthenticationGateTests : IClassFixture<KestrelTestFactory>
{
    private const string DevAdminPassword = IdentityInitializationService.DevelopmentBootstrapPassword;

    private readonly KestrelTestFactory _factory;

    public AuthenticationGateTests(KestrelTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Events_WithoutAuthentication_IsRejectedWith401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Users_WithoutAuthentication_IsRejectedWith401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutSession_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsPubliclyReachable()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithBootstrapAdmin_IssuesHardenedSessionCookie()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = DevAdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies), "No Set-Cookie header.");
        var cookie = string.Join("; ", cookies!);
        Assert.StartsWith("kestrel_auth=", cookie);
        Assert.Contains("httponly", cookie);
        Assert.Contains("samesite=strict", cookie);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401_WithGenericError()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = "Wrong-Password-123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_credentials", body);
    }

    [Fact]
    public async Task Login_WithUnknownUser_Returns401_WithSameGenericError()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = $"ghost_{Guid.NewGuid():N}", password = "Whatever-123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_credentials", body);
    }

    [Fact]
    public async Task Me_AfterLogin_ReturnsUsernameAndRoles()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = DevAdminPassword });

        var me = await client.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Equal("admin", me!.username);
        Assert.Contains("Admin", me.roles);
    }

    [Fact]
    public async Task Login_LocksOutAfterFiveFailures_EvenWithTheCorrectPassword()
    {
        var adminClient = _factory.CreateClient();
        await adminClient.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = DevAdminPassword });

        var username = $"lockout_{Guid.NewGuid():N}";
        var created = await adminClient.PostAsJsonAsync("/api/users",
            new { username, password = "Strong-Viewer-Pass-1!", roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var victim = _factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var failed = await victim.PostAsJsonAsync("/api/auth/login",
                new { username, password = "Wrong-Password-123!" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        // Account is now locked: the correct password no longer works.
        var locked = await victim.PostAsJsonAsync("/api/auth/login",
            new { username, password = "Strong-Viewer-Pass-1!" });
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
    }

    [Fact]
    public async Task Events_WithViewerSession_Returns501Skeleton()
    {
        var adminClient = _factory.CreateClient();
        await adminClient.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = DevAdminPassword });

        var username = $"viewer_{Guid.NewGuid():N}";
        await adminClient.PostAsJsonAsync("/api/users",
            new { username, password = "Strong-Viewer-Pass-1!", roles = new[] { "Viewer" } });

        var viewer = _factory.CreateClient();
        await viewer.PostAsJsonAsync("/api/auth/login",
            new { username, password = "Strong-Viewer-Pass-1!" });

        var response = await viewer.GetAsync("/api/events");
        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
    }

    private sealed record MeDto(string id, string username, List<string> roles);
}
