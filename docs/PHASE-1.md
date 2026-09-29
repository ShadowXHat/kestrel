# Phase 1 — API Skeleton + Auth Gate + Localhost-Only Bind

> Status: **implemented**. Scope per `TECH_STACK.md` §5: *"No endpoint serves
> data unauthenticated."*

## What ships in Phase 1

| Area | Implementation |
|------|----------------|
| API skeleton | `Kestrel.Api` (.NET 8, controllers + SignalR hubs + OpenAPI), `Kestrel.Core` (placeholder until Phase 2), `Kestrel.Tests` (xUnit) |
| Auth store | ASP.NET Core Identity backed by SQLite via EF Core (`data/kestrel.db`) |
| Authentication | **Cookie** sessions (`kestrel_auth`): HttpOnly, SameSite=Strict, Secure=SameAsRequest, 8 h sliding expiry. No JWT, no localStorage |
| Auth gate | `AuthorizationOptions.FallbackPolicy` requires an authenticated user on **every** endpoint unless explicitly `[AllowAnonymous]` |
| RBAC | Roles `Viewer < Analyst < Admin` + hierarchy policies enforced **server-side** |
| No self-signup | Accounts are created by Admins only. Single-admin bootstrap on first run |
| Localhost-only | `BindingGuard` fails fast at startup unless all bind addresses are loopback; override via `KestrelApp:AllowNetworkBinding=true` |
| Brute-force resistance | Login rate limit (10/min/IP default) + Identity lockout (5 fails → 15 min) |
| Session hygiene | Security-stamp re-validation — password changes kill outstanding sessions |
| Audit trail | Serilog structured logs (console + rolling file `logs/kestrel-.log`): logins, lockouts, user changes, bootstrap, guard refusals |
| Skeletons | `GET /api/events` (501 — proves the gate), authenticated `EventsHub`/`AlertsHub`, anonymous `GET /health` (serves no data) |

## Endpoints

| Method | Route | Auth | Notes |
|--------|-------|------|-------|
| POST | `/api/auth/login` | anonymous | `{ username, password }`. Rate-limited. Generic `invalid_credentials` (no user enumeration) |
| POST | `/api/auth/logout` | authenticated | Clears the session. 204 |
| GET | `/api/auth/me` | authenticated | `{ id, username, roles[] }` |
| POST | `/api/auth/change-password` | authenticated | `{ currentPassword, newPassword }`. Ends the session on success |
| GET | `/api/users` | **Admin** | User list (id, username, roles, lockout state) |
| POST | `/api/users` | **Admin** | Create user `{ username, password, roles[] }` (roles optional, default `Viewer`) |
| PUT | `/api/users/{id}/roles` | **Admin** | Replace roles. 409 if it would demote the **last** admin |
| DELETE | `/api/users/{id}` | **Admin** | 409 on self-delete or last-admin delete |
| POST | `/api/users/{id}/unlock` | **Admin** | Clear lockout + failed-attempt counter |
| GET | `/api/events` | Viewer+ | 501 skeleton (Phase 2/3) |
## Configuration

All app settings live under the `KestrelApp` section (deliberately **not**
`Kestrel` — that section belongs to the Kestrel web server itself):

| Key | Default | Meaning |
|-----|---------|---------|
| `KestrelApp:AllowNetworkBinding` | `false` | When `false`, startup **refuses** any non-loopback bind address |
| `KestrelApp:RateLimit:Auth:PermitLimit` | `10` | Login attempts per window |
| `KestrelApp:RateLimit:Auth:WindowSeconds` | `60` | Rate-limit window |
| `KestrelApp:Identity:Password:RequiredLength` | `12` | Plus digit/upper/lower/symbol requirements |
| `KestrelApp:Identity:Lockout:MaxFailedAccessAttempts` | `5` | Lockout threshold |
| `KestrelApp:Identity:Lockout:DurationMinutes` | `15` | Lockout duration |
| `KestrelApp:Seed:AdminUsername` | `admin` | Bootstrap admin username |
| `KestrelApp:Seed:AdminPassword` | *(empty)* | Bootstrap admin password — **required in production** |
| `ConnectionStrings:DefaultConnection` | `Data Source=data/kestrel.db` | Identity DB (absolutized against the content root) |

Bind addresses follow Kestrel's usual precedence: `ASPNETCORE_URLS` (env) >
`DOTNET_URLS` (env) > `Urls` key > `Kestrel:Endpoints:{name}:Url`.

## First run (admin bootstrap)

- **Development:** when the identity store is empty, an `admin` account is
  created automatically with the password `Dev#Admin-2026!` and a **loud
  warning** is logged. Change it immediately via `POST /api/auth/change-password`.
- **Production:** startup logs an **error** and skips bootstrap unless
  `KestrelApp:Seed:AdminPassword` is set (e.g. `KestrelApp__Seed__AdminPassword`
  as an environment variable). There is no default password — no silent
  credentials, ever.

## Running

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project Kestrel.Api/Kestrel.Api.csproj   # binds http://127.0.0.1:5000
```

OpenAPI UI (Development only): `http://127.0.0.1:5000/swagger`

## Tests

```bash
cd backend
dotnet test
```

Integration tests host the real API with an in-memory SQLite store and cover:
401 on every protected endpoint without a session; hardened cookie flags;
generic login failures; 5-strikes lockout; role hierarchy (Viewer gets 403);
Admin-only user management; last-admin and self-delete guards; lockout view +
unlock; and the binding-guard decision matrix.

## Deliberate security posture (what Phase 1 does NOT do)

- **TLS** arrives in Phase 7 — the binding guard warns when a network-facing
  bind is allowed without TLS. Until then keep the tool on `127.0.0.1`.
- **nginx deployment** (compose/TLS termination/security headers) is Phase 12.
- **SameSite=Strict + JSON-only API** is the CSRF mitigation; dedicated
  antiforgery is not wired for API endpoints in Phase 1.
- **EF migrations** are not yet generated (no `dotnet ef` tooling was run).
  Schema is created via `EnsureCreated` until then. Run
  `dotnet ef migrations add InitialIdentity` before storing real data — dev
  DBs are disposable by policy (no real case data).

## Files

```
backend/Kestrel.Api/
├── Program.cs                          # composition: Serilog, Identity, cookies, policies, guard
├── Auth/
│   ├── AppUser.cs                      # IdentityUser (forward-compatible)
│   ├── AppDbContext.cs                 # Identity EF Core store
│   ├── Roles.cs                        # role + policy name constants
│   └── IdentityInitializationService.cs # schema, role seed, single-admin bootstrap
├── Security/BindingGuard.cs            # localhost-only config guard
├── Controllers/
│   ├── AuthController.cs               # login / logout / me / change-password
│   ├── UsersController.cs              # Admin user management + guards
│   └── EventsController.cs             # 501 skeleton proving the gate
├── Hubs/EventsHub.cs                   # authenticated SignalR skeletons
└── Swagger/CookieAuthOperationFilter.cs
```
