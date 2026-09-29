// =============================================================================
// Kestrel.Api — Phase 1: API skeleton + auth gate + localhost-only bind
//
// Security gates (TECH_STACK.md §5 / WHY_THIS_STACK.md §17):
//   1. FallbackPolicy requires an authenticated user on EVERY endpoint unless
//      explicitly [AllowAnonymous]. No build serves event data unauthenticated.
//   2. BindingGuard refuses non-loopback binds unless
//      KestrelApp:AllowNetworkBinding=true (config guard, not a doc note).
//      Network-facing additionally requires TLS (Phase 7) + nginx (Phase 12).
//   3. Cookie auth (HttpOnly, SameSite=Strict). No JWT / localStorage.
// =============================================================================

using Microsoft.AspNetCore.Authorization;

using Kestrel.Api.Auth;
using Kestrel.Api.Hubs;
using Kestrel.Api.Security;
using Kestrel.Api.Swagger;
using Kestrel.Core.Ingest;
using Kestrel.Core.Jobs;
using Kestrel.Core.Search;
using Kestrel.Core.Storage;
using Kestrel.Core.Export;
using Kestrel.Core.Detection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ---- Upload friendliness: EVTX files are large -------------------------------
// Kestrel's default minimum request data rate aborts slow uploads; large EVTX
// staging must not depend on link speed. The actual byte cap is enforced
// per-request by the ingest endpoint (streamed, never buffered into memory).
builder.WebHost.ConfigureKestrel(options => options.Limits.MinRequestBodyDataRate = null);

// ---- Runtime directories (SQLite identity store + rolling logs) -------------
// Anchored to the content root so the store location does not depend on the
// process working directory.
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "data");
var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "logs");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(logDirectory);

// ---- Serilog: structured logging (console + rolling file) -------------------
// The audit trail of the tool itself: logins, user changes, guard refusals.
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(logDirectory, "kestrel-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate:
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

// ---- Identity store (SQLite via EF Core) ------------------------------------
// "Data Source=data/kestrel.db" is content-root-relative and absolutized below
// so the store location is deterministic regardless of the process CWD.
var identityDbFilePath = ExtractDbFilePath(builder.Configuration.GetConnectionString("DefaultConnection"))
                         ?? "kestrel.db";
if (!Path.IsPathRooted(identityDbFilePath))
{
    identityDbFilePath = Path.Combine(builder.Environment.ContentRootPath, identityDbFilePath);
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={identityDbFilePath}"));

// ---- Phase 2: event store connection (same SQLite file, dedicated pool) ------
// Shares data/kestrel.db with the identity store but opens its own connections
// (Microsoft.Data.Sqlite, WAL, foreign keys) — see docs/PHASE-2.md.
var eventsDbFilePath = ExtractDbFilePath(builder.Configuration.GetConnectionString("EventsConnection"))
                       ?? identityDbFilePath;
if (!Path.IsPathRooted(eventsDbFilePath))
{
    eventsDbFilePath = Path.Combine(builder.Environment.ContentRootPath, eventsDbFilePath);
}
// ---- ASP.NET Core Identity + role-based authorization -----------------------
// Roles: Viewer < Analyst < Admin. Enforced server-side via policies
// (see Policies.cs); never trusted from the client.
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = false;
        options.SignIn.RequireConfirmedAccount = false;

        var cfg = builder.Configuration;
        options.Password.RequiredLength = cfg.GetValue("KestrelApp:Identity:Password:RequiredLength", 12);
        options.Password.RequireDigit = cfg.GetValue("KestrelApp:Identity:Password:RequireDigit", true);
        options.Password.RequireUppercase = cfg.GetValue("KestrelApp:Identity:Password:RequireUppercase", true);
        options.Password.RequireLowercase = cfg.GetValue("KestrelApp:Identity:Password:RequireLowercase", true);
        options.Password.RequireNonAlphanumeric = cfg.GetValue("KestrelApp:Identity:Password:RequireNonAlphanumeric", true);

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = cfg.GetValue("KestrelApp:Identity:Lockout:MaxFailedAccessAttempts", 5);
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(cfg.GetValue("KestrelApp:Identity:Lockout:DurationMinutes", 15));
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager<SignInManager<AppUser>>();

// ---- Cookie authentication (no JWT, no localStorage — see .clinerules) ------
builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "kestrel_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Plain http is only ever acceptable on loopback (dev). In production
        // nginx terminates TLS and X-Forwarded-Proto marks requests https.
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // API, not a page app: return 401/403 instead of redirecting.
        // Periodic stamp re-validation makes password changes kill old sessions.
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync,
            OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
        };
    });

// ---- Authorization: default deny + role-hierarchy policies ------------------
builder.Services.AddAuthorization(options =>
{
    // Phase 1 auth gate: everything requires an authenticated user unless
    // explicitly [AllowAnonymous].
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Role hierarchy: Admin implies Analyst implies Viewer.
    options.AddPolicy(Policies.Viewer, p => p.RequireRole(Roles.Viewer, Roles.Analyst, Roles.Admin));
    options.AddPolicy(Policies.Analyst, p => p.RequireRole(Roles.Analyst, Roles.Admin));
    options.AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));
});

// ---- Rate limiting on auth endpoints (brute-force resistance) ---------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("auth", window =>
    {
        window.PermitLimit = builder.Configuration.GetValue("KestrelApp:RateLimit:Auth:PermitLimit", 10);
        window.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("KestrelApp:RateLimit:Auth:WindowSeconds", 60));
        window.QueueLimit = 0;
    });
});

// ---- Reverse-proxy awareness (nginx TLS termination ships in Phase 12) ------
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Phase 12 pins the loopback nginx proxy in KnownProxies.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---- MVC + SignalR + OpenAPI -------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Kestrel API",
        Version = "v1",
        Description = "Windows Event Log analyzer API. Every endpoint except /health requires an authenticated session cookie."
    });
    options.AddSecurityDefinition("cookie", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Cookie,
        Name = "kestrel_auth",
        Description = "Session cookie issued by POST /api/auth/login."
    });
    options.OperationFilter<CookieAuthOperationFilter>();
});

// ---- Phase 2: event store + EVTX ingest pipeline ------------------------------
// The event store shares the identity database file through its own connection
// pool (EventDatabase). Ingestion runs in a bounded background queue with a
// per-job timeout — uploads only stage the file and enqueue (202).
builder.Services.AddSingleton<EventDatabase>(sp =>
    new EventDatabase(eventsDbFilePath, sp.GetRequiredService<ILogger<EventDatabase>>()));
builder.Services.AddSingleton(sp =>
    IngestOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>(), builder.Environment.ContentRootPath));
builder.Services.AddSingleton<EventStore>();
builder.Services.AddSingleton<IngestJobStore>();
builder.Services.AddSingleton<IEvtxIngestService, EvtxIngestService>();
builder.Services.AddSingleton<IIngestProgressSink, EventsHubIngestProgressSink>();
builder.Services.AddSingleton<IngestJobQueue>();
builder.Services.AddSingleton<SearchService>();
builder.Services.AddSingleton<ExportService>();

// ---- Phase 5: Sigma detection ------------------------------------------------
// The rule pack loads from Sigma:RulePackPath (content-root relative, "rules"
// by default). Per-file failures are recorded on the pack, never silently
// dropped; the ATT&CK store lazily loads Attack:StixFilePath and warns when the
// intel bundle is absent (rules still carry raw technique IDs).
builder.Services.AddSingleton(sp =>
    SigmaOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>(), builder.Environment.ContentRootPath));
builder.Services.AddSingleton(sp =>
    AttackIntelOptions.FromConfiguration(sp.GetRequiredService<IConfiguration>(), builder.Environment.ContentRootPath));
builder.Services.AddSingleton<AttackTechniqueStore>();
builder.Services.AddSingleton<RulePack>();
builder.Services.AddSingleton<DetectionEngine>();

// ---- First-run initialization: schema + roles + single-admin bootstrap -------
// Hosted services start before the HTTP server binds, so the auth store is
// ready before the first request. The event store schema is created next, and
// the ingest processor starts last (it needs both).
builder.Services.AddHostedService<IdentityInitializationService>();
builder.Services.AddHostedService<EventStoreInitializationService>();
builder.Services.AddHostedService<IngestJobProcessor>();

var app = builder.Build();


// ---- Phase 1 binding guard: localhost-only by default ------------------------
// Fails fast (before anything binds) if a non-loopback address is configured
// without the explicit override. See docs/PHASE-1.md.
BindingGuard.Enforce(app.Configuration, app.Logger);

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHub<EventsHub>("/hubs/events");
app.MapHub<AlertsHub>("/hubs/alerts");

// Liveness probe for the deployment layer. Serves no data and reveals nothing —
// the only deliberately anonymous endpoint; everything else is gated by the
// FallbackPolicy.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .AllowAnonymous();

app.Run();

/// <summary>Pulls the file path out of a SQLite connection string ("Data Source=...").</summary>
static string? ExtractDbFilePath(string? connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return null;
    }

    foreach (var part in connectionString.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        var separatorIndex = part.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            continue;
        }

        if (part[..separatorIndex].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
        {
            return part[(separatorIndex + 1)..].Trim();
        }
    }

    return null;
}

// Exposes the implicit Program class for WebApplicationFactory-based tests.
public partial class Program { }

