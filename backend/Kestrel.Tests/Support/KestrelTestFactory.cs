using Kestrel.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kestrel.Tests.Support;

/// <summary>
/// Hosts the real Kestrel.Api in-memory with an in-memory SQLite identity store.
/// Runs in the Development environment so the single-admin bootstrap applies.
/// </summary>
public sealed class KestrelTestFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // Login rate limit is raised for tests; lockout behaviour is tested
        // separately against the identity store itself.
        builder.UseSetting("KestrelApp:RateLimit:Auth:PermitLimit", "1000");

        builder.ConfigureTestServices(services =>
        {
            _connection.Open();

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptionsBuilder<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
