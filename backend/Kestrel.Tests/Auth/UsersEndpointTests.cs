using System.Net;
using System.Net.Http.Json;
using Kestrel.Api.Auth;
using Kestrel.Tests.Support;
using Xunit;

namespace Kestrel.Tests.Auth;

/// <summary>
/// User administration contract: Admin-only surface, no open self-signup,
/// last-admin and self-delete guards, per-rule validation errors.
/// </summary>
public class UsersEndpointTests : IClassFixture<KestrelTestFactory>
{
    private const string DevAdminPassword = IdentityInitializationService.DevelopmentBootstrapPassword;
    private const string ViewerPassword = "Strong-Viewer-Pass-1!";

    private readonly KestrelTestFactory _factory;

    public UsersEndpointTests(KestrelTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Anonymous_CannotListUsers()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_CannotCreateUsers_NoOpenSignup()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/users",
            new { username = "sneaky_user", password = ViewerPassword, roles = new[] { "Admin" } });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CreatesViewer_WhoCanLogIn()
    {
        var admin = await LoginAdminAsync();
        var username = $"viewer_{Guid.NewGuid():N}";

        var created = await admin.PostAsJsonAsync("/api/users",
            new { username, password = ViewerPassword, roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var viewer = _factory.CreateClient();
        var login = await viewer.PostAsJsonAsync("/api/auth/login",
            new { username, password = ViewerPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await viewer.GetFromJsonAsync<MeDto>("/api/auth/me");
        Assert.NotNull(me);
        Assert.Contains("Viewer", me!.roles);
    }

    [Fact]
    public async Task Viewer_CannotListUsers_403()
    {
        var viewer = await CreateViewerAndLoginAsync();
        var response = await viewer.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_CannotCreateUsers_403()
    {
        var viewer = await CreateViewerAndLoginAsync();
        var response = await viewer.PostAsJsonAsync("/api/users",
            new { username = $"nope_{Guid.NewGuid():N}", password = ViewerPassword, roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WeakPassword_IsRejected_WithRuleErrors()
    {
        var admin = await LoginAdminAsync();
        var response = await admin.PostAsJsonAsync("/api/users",
            new { username = $"weak_{Guid.NewGuid():N}", password = "short", roles = new[] { "Viewer" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("errors", body);
    }

    [Fact]
    public async Task UnknownRole_IsRejected()
    {
        var admin = await LoginAdminAsync();
        var response = await admin.PostAsJsonAsync("/api/users",
            new { username = $"badrole_{Guid.NewGuid():N}", password = ViewerPassword, roles = new[] { "SuperAdmin" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemovingTheLastAdmin_IsRefused_409()
    {
        var admin = await LoginAdminAsync();
        var adminId = await FindUserIdAsync(admin, "admin");

        var response = await admin.PutAsJsonAsync($"/api/users/{adminId}/roles",
            new { roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SecondAdmin_CanBeDemoted()
    {
        var admin = await LoginAdminAsync();
        var username = $"admin_{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/users",
            new { username, password = ViewerPassword, roles = new[] { "Admin" } });

        var secondId = await FindUserIdAsync(admin, username);
        var response = await admin.PutAsJsonAsync($"/api/users/{secondId}/roles",
            new { roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CannotDeleteSelf_409()
    {
        var admin = await LoginAdminAsync();
        var adminId = await FindUserIdAsync(admin, "admin");

        var response = await admin.DeleteAsync($"/api/users/{adminId}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Lockout_IsVisibleInUserList_AndUnlockClearsIt()
    {
        var admin = await LoginAdminAsync();
        var username = $"locked_{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/users",
            new { username, password = ViewerPassword, roles = new[] { "Viewer" } });

        var victim = _factory.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await victim.PostAsJsonAsync("/api/auth/login",
                new { username, password = "Wrong-Password-123!" });
        }

        var userId = await FindUserIdAsync(admin, username);
        var lockedRow = await GetUserRowAsync(admin, userId);
        Assert.NotNull(lockedRow!.lockoutEndUtc);

        var unlock = await admin.PostAsync($"/api/users/{userId}/unlock", content: null);
        Assert.Equal(HttpStatusCode.NoContent, unlock.StatusCode);

        var clearedRow = await GetUserRowAsync(admin, userId);
        Assert.Null(clearedRow!.lockoutEndUtc);
    }

    private async Task<HttpClient> LoginAdminAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { username = "admin", password = DevAdminPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private async Task<HttpClient> CreateViewerAndLoginAsync()
    {
        var admin = await LoginAdminAsync();
        var username = $"viewer_{Guid.NewGuid():N}";
        var created = await admin.PostAsJsonAsync("/api/users",
            new { username, password = ViewerPassword, roles = new[] { "Viewer" } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var viewer = _factory.CreateClient();
        var login = await viewer.PostAsJsonAsync("/api/auth/login",
            new { username, password = ViewerPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return viewer;
    }

    private async Task<string> FindUserIdAsync(HttpClient admin, string username)
    {
        var users = await admin.GetFromJsonAsync<List<UserRow>>("/api/users");
        Assert.NotNull(users);
        var row = users!.Single(u => u.username == username);
        return row.id;
    }

    private async Task<UserRow?> GetUserRowAsync(HttpClient admin, string id)
    {
        var users = await admin.GetFromJsonAsync<List<UserRow>>("/api/users");
        return users!.Single(u => u.id == id);
    }

    private sealed record MeDto(string id, string username, List<string> roles);

    private sealed record UserRow(
        string id,
        string username,
        List<string> roles,
        DateTimeOffset? lockoutEndUtc,
        int accessFailedCount);
}
