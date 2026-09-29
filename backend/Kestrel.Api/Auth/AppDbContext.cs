using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kestrel.Api.Auth;

/// <summary>
/// Identity store (users + roles). The Phase 2 event store lives in
/// Kestrel.Core and uses its own connection to the same SQLite database.
/// </summary>
public class AppDbContext : IdentityDbContext<AppUser, IdentityRole, string>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
    }
}
