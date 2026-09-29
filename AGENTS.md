# Kestrel Agent Instructions

## Project Overview
Kestrel is a **web-based Windows Event Log analyzer** for SOC analysts and incident responders.

## Technology Stack
- **Backend**: ASP.NET Core Web API (C#, .NET 8 LTS)
- **Frontend**: React 18 + TypeScript + Vite + AG Grid + ECharts + Tailwind CSS
- **Storage**: SQLite (Microsoft.Data.Sqlite) + FTS5 trigram tokenizer
- **Detection**: Custom Sigma rule matcher (C#)
- **Threat Intel**: MITRE ATT&CK (embedded STIX JSON)
- **Real-time**: SignalR (WebSockets)
- **Auth**: ASP.NET Core Identity + cookie auth (RBAC: Viewer/Analyst/Admin)
- **Deploy**: Docker + nginx (TLS-terminating proxy)

## Key Architecture Decisions
1. **Web over desktop** — cross-platform reach, browser memory limits rule out WASM
2. **C# over Rust/Python/Go** — no GIL, native EVTX API
3. **React + AG Grid** — best 100k+ row grid for the web
4. **Native Eventing.Reader** — free message formatting, Microsoft-maintained
5. **SQLite FTS5 over Lucene.NET** — zero-config, embedded, BM25 ranking
6. **Custom Sigma matcher** — no production C# Sigma engine exists (~2-4k LOC)
7. **Auth is Phase 1** — no endpoint serves data unauthenticated
8. **Localhost-only by default** — requires auth + TLS to go network-facing
9. **Forward-compatible schema** — FP field, role fields exist from day one

## Development Phases (12 total)
1. API skeleton + auth gate + localhost-only bind
2. Forward-compatible SQLite schema + EVTX ingest
3. React frontend + AG Grid event browser
4. SQLite FTS5 search + export
5. Sigma matcher + rule pack + compile/validate
6. ATT&CK integration
7. HTTPS/TLS + security hardening
8. Error observability + Serilog
9. FP tuning UI + rule lifecycle
10. Dashboards (ECharts)
11. Live monitoring (SignalR + EventLogWatcher)
12. Docker packaging + docs

## Project Structure
```
kestrel/
├── backend/
│   ├── Kestrel.Api/       # Web API + SignalR hubs + Auth + Jobs
│   ├── Kestrel.Core/      # Parsing, Storage, Search, Detection, Export
│   └── Kestrel.Tests/     # xUnit tests
├── frontend/
│   ├── src/
│   │   ├── pages/       # Login, Dashboard, Events, Search, Alerts, Settings
│   │   ├── components/  # Sidebar, AG Grid wrappers, charts
│   │   ├── api/         # Typed API client
│   │   └── store/       # Client state
│   │   └── App.tsx      # Main router
│   ├── package.json
│   ├── vite.config.ts
│   └── tsconfig.json
├── deploy/
│   ├── Dockerfile
│   ├── nginx.conf
│   └── docker-compose.yml
└── docs/                # Documentation
```

## Hard Rules (non-negotiable)
- Auth gates every endpoint before serving event data
- Localhost-only by default; network binding requires auth + TLS
- No silent failures — all errors surfaced with reasons
- Magic-byte validation on EVTX uploads
- No real case data until backups exist
- Schema forward-compatible from Phase 2
- Do NOT format messages in the hot path
- Streaming uploads, never buffer into memory
- Structured logging with Serilog for audit trail
