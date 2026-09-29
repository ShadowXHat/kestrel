using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kestrel.Api.Auth;

/// <summary>
/// First-run initialization, run before the HTTP server binds:
///   1. Applies EF migrations when present, else creates the identity schema
///      (see docs/PHASE-1.md — generate migrations with `dotnet ef` before
///      storing any real data).
///   2. Seeds the Viewer/Analyst/Admin roles.
///   3. Bootstraps a single admin account when the store is empty:
///        - Production: requires KestrelApp:Seed:AdminPassword
///          (env: KestrelApp__Seed__AdminPassword). No silent default.
///        - Development: creates the well-known dev admin with a LOUD warning.
/// No open self-signup exists anywhere else in the product.
/// </summary>
public class IdentityInitializationService : IHostedService
{
    /// <summary>Well-known Development-only bootstrap password (meets the password policy).</summary>
    public const string DevelopmentBootstrapPassword = "Dev#Admin-2026!";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<IdentityInitializationService> _logger;

    public IdentityInitializationService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<IdentityInitializationService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        // 1. Schema.
        var migrations = db.Database.GetMigrations().ToList();
        if (migrations.Count > 0)
        {
            await db.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Identity schema: {Count} migrations applied.", migrations.Count);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            _logger.LogInformation(
                "Identity schema ensured (no EF migrations defined yet — run `dotnet ef migrations add` before real deployments).");
        }

        // 2. Roles.
        foreach (var role in Roles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            var created = await roleManager.CreateAsync(new IdentityRole(role));
            if (!created.Succeeded)
            {
                var reasons = string.Join("; ", created.Errors.Select(e => $"{e.Code}: {e.Description}"));
                _logger.LogError("Role seed failed for '{Role}': {Reasons}", role, reasons);
                throw new InvalidOperationException($"Role seed failed for '{role}': {reasons}");
            }

            _logger.LogInformation("Role seeded: {Role}", role);
        }

        // 3. Single-admin bootstrap (only when the store is empty).
        var userCount = await userManager.Users.CountAsync(cancellationToken);
        if (userCount > 0)
        {
            _logger.LogInformation("Identity store initialized ({UserCount} users); bootstrap skipped.", userCount);
            return;
        }

        var username = _configuration["KestrelApp:Seed:AdminUsername"];
        if (string.IsNullOrWhiteSpace(username))
        {
            username = "admin";
        }

        var password = _configuration["KestrelApp:Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(password))
        {
            if (!_environment.IsDevelopment())
            {
                _logger.LogError(
                    "Bootstrap: no admin can be created — set KestrelApp:Seed:AdminPassword " +
                    "(env: KestrelApp__Seed__AdminPassword) and restart. The API will run, but nobody can log in " +
                    "until an admin exists.");
                return;
            }

            password = DevelopmentBootstrapPassword;
            _logger.LogWarning(
                "Bootstrap: DEVELOPMENT default admin '{Username}' created with password '{Password}'. " +
                "CHANGE THIS PASSWORD before any use beyond local testing.",
                username, password);
        }

        var admin = new AppUser { UserName = username };
        var createResult = await userManager.CreateAsync(admin, password);
        if (!createResult.Succeeded)
        {
            var reasons = string.Join("; ", createResult.Errors.Select(e => $"{e.Code}: {e.Description}"));
            _logger.LogError("Admin bootstrap failed for '{Username}': {Reasons}", username, reasons);
            throw new InvalidOperationException($"Admin bootstrap failed: {reasons}");
        }

        await userManager.AddToRolesAsync(admin, Roles.All);
        _logger.LogInformation("Bootstrap: admin '{Username}' created with roles [{Roles}].",
            username, string.Join(", ", Roles.All));
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
