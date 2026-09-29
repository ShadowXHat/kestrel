# Kestrel — Tech Stack

> **Kestrel** is a **web-based** Windows Event Log analyzer for SOC analysts and
> incident responders. This document describes **what** we use. For the
> **why**, see [WHY_THIS_STACK.md](./WHY_THIS_STACK.md).
>
> *Repo/package handle suggestion: `kestrel-dfir` or `kestrel-evtx` (plain
> "Kestrel" collides with the Kestrel web server that ships inside ASP.NET
> Core itself — expect that in search results and when naming the API
> project).*

---

## 1. Overview

| Layer | Technology |
|-------|------------|
| Backend | ASP.NET Core Web API (C#, .NET 8 LTS) |
| Frontend | React 18 + TypeScript + Vite |
| Data Grid | AG Grid (Community Edition) |
| Charts | Apache ECharts |
| Styling | Tailwind CSS (dark SOC theme) |
| Real-time Push | SignalR (WebSockets) |
| Log Parsing | System.Diagnostics.Eventing.Reader |
| Storage | SQLite (Microsoft.Data.Sqlite) |
| Search | SQLite FTS5 (contentless, trigram tokenizer) |
| Detection Engine | Custom Sigma rule matcher (C#) |
| Threat Knowledge | MITRE ATT&CK (embedded STIX JSON) |
| Authentication | ASP.NET Core Identity + cookie auth |
| Authorization | Role separation (Viewer / Analyst / Admin) |
| Logging (self) | Serilog (structured logs, file + console) |
| Durability | SQLite WAL mode (scheduled backups deferred) |
| Deployment | Docker (nginx + ASP.NET Core, TLS-terminating proxy) |

**Target platform:** Self-hosted web app — any modern browser (Chrome, Edge, Firefox).
**Host:** Any Windows or Linux machine (even a laptop for single-analyst use).
**Baseline security:** HTTPS enforced, authenticated & role-gated access, hardened EVTX upload.

---

## 2. Core Features

### 2.1 Parsing & Importing
- Parses **offline .evtx files** uploaded through the web UI or dropped
  into an ingestion folder on the server, via the native Windows
  `System.Diagnostics.Eventing.Reader` API (`EventLogQuery` +
  `PathType.FilePath`).
- Ingests **live Windows Event Logs** (Security, System, Application, Sysmon)
  from the host machine through `EventLogWatcher` with bookmark-based resume
  (e.g. for a forwarding collector setup).
- Handles multiple log sources at once (multi-file indexing, deduplication).
- Import is a **background job** — the user keeps working in the browser
  while parsing happens server-side.

### 2.2 Event Browser (Web Data Grid)
- **AG Grid** table showing parsed events: timestamp, event ID, channel,
  user, computer, level, and rendered message.
- Row virtualization — smooth paging/virtual scrolling through 100k+ rows
  in the browser (only visible rows are rendered).
- Server-side sorting, grouping, and column filtering.
- Click a row → detail panel with the full **lazily-rendered message**
  (raw XML is parsed eagerly; human-readable messages are only formatted
  when requested — avoids the 6-minute-vs-0.11s formatting trap).

### 2.3 Advanced Search & Filtering
- Structured filters: Event ID, channel, user, computer, time range —
  executed server-side.
- Full-text search via **SQLite FTS5** with **trigram tokenizer** —
  substring hunts like `*mimikatz*`.
- Regex and wildcard matching on fields (Sigma-style modifiers).
- Recent searches saved per user; shareable search URLs (bookmarkable).

### 2.4 Threat Detection (Sigma Rules)
- Supports **Sigma rule format v1/v2** (YAML-based detection rules).
- Ships a curated detection pack mapped to Windows Event Logs
  (logon auditing, process creation, persistence, credential access).
- Pre-filter by **channel + Event ID** before full matching (20%–10x faster).
- Wildcard patterns compiled to `contains`/`startsWith`/`endsWith`
  (**FastMatch**) so regex is avoided in the hot path.

### 2.5 MITRE ATT&CK Integration
- Embeds a **pinned** `enterprise-attack.json` (STIX 2.1) in the backend
  (~350 techniques, ~470 sub-techniques, ~14 tactics).
- Parsed once at startup into an in-memory index.
- Every detection is enriched with `attack.*` tags from its Sigma rule.
- Ships a curated **Event ID → Technique** mapping table as a supplement.
- Exports **ATT&CK Navigator layers** + interactive Navigator matrix view.

### 2.6 Dashboards & Visualization (ECharts)
- Timeline view of events over time.
- Event distribution charts (by event ID, level, user, technique).
- Detection summary: matched rules, levels, affected hosts, IPs.
- Live-updating widgets via SignalR push (real-time mode).
- ATT&CK coverage heatmap rendered in-browser.

### 2.7 Alerting
- Flags suspicious events: failed logons (4625), account lockouts (4740),
  lateral movement indicators (4648), privilege escalation (4672), etc.
- Rule-based alerting with severity levels (info/low/medium/high/critical).
- Alert inbox with live browser notifications (SignalR).
- Alert history persisted in SQLite.

### 2.8 Export & API
- Export to **CSV, JSON, JSONL** (composable with other tools:
  KAPE, Timeline Explorer, jq).
- ATT&CK Navigator layer JSON export.
- **REST API** for every feature — allows scripting and integration
  with other tools (SIEM, SOAR, ticketing).

### 2.9 Security & Access Control
> Implemented in **Phase 1** — before any endpoint exposes event data.
> There is no unauthenticated build of this tool.
- **Authentication:** ASP.NET Core Identity with cookie sessions.
  Users must log in — no anonymous access to event data.
- **Role separation (RBAC):**
  - `Viewer` — browse, search, export, see alerts.
  - `Analyst` — Viewer + upload logs, run/disable rules, mark false positives.
  - `Admin` — Analyst + manage users/roles, rule-pack updates,
    view all logs (incl. of the tool itself).
- **Registration:** admin-created accounts or single admin bootstrap.
  No open self-signup.
- **HTTPS/TLS:** production enforces HTTPS via the nginx reverse proxy
  (Let's Encrypt or internal CA), TLS 1.2+ only. HTTP is rejected.
- **Security headers:** Content-Security-Policy, X-Frame-Options,
  X-Content-Type-Options, HSTS set on all responses.
- **Brush: securing the upload path (see 2.10) is part of access control** —
  only authenticated Analysts/Admins may upload files.

### 2.10 Hardening — Upload, Parsing, & Failure Handling
- **EVTX upload validation:**
  - Streamed upload to a temp location (no loading whole file into memory).
  - Configurable size limit per file (default e.g. 5 GB).
  - **Magic-byte verification**: EVTX files must start with the
    `ElfFile\x00` header — other files are rejected before parsing.
  - Parse happens in a **background job**; a malformed/corrupt record
    is captured as a per-record error, not a crash.
- **Sandboxing the parse:** parsing runs in a worker process with
  bounded memory; a never-ending parse is killed by a job timeout.
- **Error observability:**
  - Every parse reports `total records / parsed / skipped / failed`.
  - Failed records are surfaced in the UI ("N records unparseable")
    with sample reasons — never silent failure.
  - Sigma rule exceptions are caught per rule: a bad rule is **disabled
    and reported**, it never stops a scan.
- **App-level logging:** Serilog structured logs (info/warn/error) for
  auth events, uploads, rule updates — so the tool can be audited like a
  security appliance.

### 2.11 Detection Quality — False Positives & Rule Updates
> The FP-status field ships in the **Phase 2 schema**; the tuning **UI** arrives
> in Phase 10. The data model is ready from day one so no migration onto live
> data is needed.
- **False-positive tuning:**
  - Analysts can mark a detection as a **false positive** with an optional
    reason; it stays dismissed for that event+rule combo on re-runs.
  - Per-rule statistics (hits, dismissed, FP rate) shown in the UI so
    noisy rules are easy to spot.
  - Rule suggestions: analysts can flag a rule as "too noisy" for review.
- **Sigma rule-pack lifecycle:**
  - Rules carry metadata: source, version, SHA-256, upstream URL.
  - **Staged updates**: new/updated rules land in *Draft* state — compiled
    and validated against the matcher but not active. Admin activates
    them in bulk.
  - Rules that fail to compile are **quarantined with the error shown** —
    they never silently break the matcher.
  - The matcher pins a Sigma spec version; unsupported syntax is detected
    at compile time, not at scan time.

---

## 3. Technologies — Details

### 3.1 ASP.NET Core Web API (C#, .NET 8 LTS)
- Backend service: REST API + background job queue + SignalR hubs.
- Async/await throughout — non-blocking file parsing and indexing.
- TPL (`Parallel.ForEach`, `Channel<T>`) for true multithreaded parsing —
  .NET has **no GIL**, so cores are used for real parallelism.
- Kestrel web server — fast, cross-platform, no IIS requirement.
- Has OpenAPI (Swagger) documentation out of the box.

### 3.2 React 18 + TypeScript + Vite
- Component-based SPA rendered in the browser.
- TypeScript adds type safety on the API boundary (shared DTOs).
- Vite dev server → instant hot reload during development.
- Served as static files by the backend (single deployable) or nginx.

### 3.3 AG Grid (Community Edition)
- The reference data-grid for the web; used by banks, Splunk-like UIs,
  and security platforms.
- **Server-side row model** — browser only holds visible rows, the
  server streams data as you scroll. Handles 100k+ rows smoothly.
- Built-in column filtering, sorting, grouping, pinning, exports.
- Free MIT-licensed Community edition is enough for this project.

### 3.4 Apache ECharts
- Battle-tested charting library (used in enterprise dashboards).
- Timeline charts, heatmaps, distributions, line/bar/pie.
- Canvas/WebGL rendering — smooth with large datasets.
- MIT-licensed, huge example gallery, active development.

### 3.5 Tailwind CSS
- Utility-first CSS for a polished dark SOC theme.
- No design system to learn; consistent dark-mode styling.
- Ships small generated CSS (purged to what's used).

### 3.6 SignalR (Real-time Push)
- ASP.NET Core built-in WebSocket library — no extra infrastructure.
- Used for live event tailing, alert notifications, and dashboard updates.
- Automatic fallback to Server-Sent Events / long polling if WebSockets
  are blocked.

### 3.7 System.Diagnostics.Eventing.Reader
- Native Windows API for reading EVTX files and live logs —
  runs **server-side**.
- Used for both **offline .evtx parsing** and **live subscription**
  (via `EventLogWatcher`).
- 190k-event Security log: ~1.8–2.5s (metadata), ~12–13s (with messages).
- Only out-of-box managed route to formatted provider messages.

### 3.8 SQLite (Microsoft.Data.Sqlite)
- Zero-configuration, single-file embedded database — runs wherever the
  server runs, including Linux Docker containers.
- Serves as the event store / evidence artifact.
- Runs in **WAL mode** (write-ahead logging) — reads don't block writes;
  crash-recovery safe. (Backups are deferred — see §6.)
- Time-range and field-index queries at sub-100ms on 1M+ events.

### 3.9 SQLite FTS5 (Trigram Tokenizer)
- Full-text search built into SQLite (native BM25 ranking).
- **Contentless FTS5** halves index size.
- **Trigram tokenizer** enables `LIKE '%substring%'` style hunts.
- Reference point: 250GB / 50M rows → ~40ms per query.

### 3.10 Custom Sigma Matcher (C#)
- Parses Sigma YAML via **YamlDotNet** into rule models.
- Compiles `detection` blocks into an AND/OR/NOT expression tree.
- Channel + Event ID pre-filter; FastMatch string ops; Aho-Corasick
  prefilter for large rule packs.
- Modeled on Hayabusa / Chainsaw (Rust) architecture — no production C#
  Sigma engine exists, so we implement a compact one (~2–4k LOC).

### 3.11 MITRE ATT&CK (Embedded STIX)
- Pinned `enterprise-attack.json` shipped with the backend (compressed ~1–2MB).
- Parsed at startup into `technique → {name, tactics, parent}` dictionary.
- Version pinned and treated as reviewed dependency.

### 3.12 Docker + nginx
- `docker-compose` for one-command deployment.
- nginx serves static frontend; reverse-proxies API + WebSockets.
- **nginx terminates TLS** (certificates via Let's Encrypt or internal CA);
  the backend itself can run in plain HTTP behind the proxy.
- Works on any Linux or Windows host with Docker.

### 3.13 ASP.NET Core Identity + Cookie Auth
- Built-in .NET auth framework — no external IdP required.
- Cookie-based sessions (natural fit for same-origin SPA served by
  the same backend; no JWT/localStorage XSS exposure).
- Role claims (Viewer/Analyst/Admin) enforced via authorization
  policies on every endpoint and SignalR hub method.

### 3.14 Serilog (Structured Logging)
- Structured logs with `Information/Warning/Error` levels.
- Sinks: rotating file + console; JSON format for easy machine parsing.
- Captures the "audit trail of the tool itself": logins, uploads,
  rule activation.

### 3.15 EVTX Upload Guard (inside the API)
- Streaming multipart upload with configurable size cap.
- **Magic-byte check** for `ElfFile\x00` before any parsing begins.
- Uploads stored in a temp/ingest area, then passed to the background
  ingestion worker with a timeout and memory bound.

---

## 4. Project Structure (planned)

```
Kestrel/
├── backend/
│   ├── Kestrel.Api/               # ASP.NET Core Web API + SignalR hubs
│   │   ├── Controllers/         # REST endpoints (auth-gated)
│   │   ├── Hubs/                # SignalR hubs (events, alerts, dashboards)
│   │   ├── Auth/                # Identity store, role policies, seed users
│   │   └── Jobs/                # background workers: ingestion
│   ├── Kestrel.Core/              # class library — parsing, search, detection
│   │   ├── Parsing/             # EVTX loader: magic-byte check, normalization
│   │   ├── Storage/             # SQLite schema (WAL), FTS5 index, migrations
│   │   ├── Search/              # filter compiler, FTS queries
│   │   ├── Detection/
│   │   │   ├── Sigma/           # rule models, matcher, compile/validate
│   │   │   └── Attck/           # STIX index, EventID→technique table
│   │   └── Export/              # CSV/JSON/JSONL writers
│   └── Kestrel.Tests/             # xUnit unit tests
├── frontend/
│   ├── src/
│   │   ├── pages/               # login, dashboard, events, search, alerts, settings, admin
│   │   ├── components/          # AG Grid wrappers, charts, sidebar, etc.
│   │   ├── api/                 # typed API client (generated from OpenAPI)
│   │   └── store/               # client state
│   ├── package.json
│   └── vite.config.ts
├── deploy/
│   ├── Dockerfile
│   ├── nginx.conf               # TLS, security headers, WebSocket proxy
│   ├── docker-compose.yml
│   └── README.md                # TLS setup, cert options
└── docs/                        # this documentation
```

---

## 5. Development Phases

| Phase | Scope | Deliverable |
|-------|-------|-------------|
| 1 | API skeleton + **auth gate** + localhost-only bind | No endpoint serves data unauthenticated |
| 2 | Forward-compatible SQLite schema + EVTX ingest (Analyst/Admin only) | Upload/parse; schema already carries FP & role fields |
| 3 | React frontend + AG Grid event browser | Browse events, sort, filter page |
| 4 | SQLite FTS5 search + export | Structured + full-text search, CSV/JSON export |
| 5 | Sigma matcher + rule pack + compile/validate | Detections flagged (records carry FP-status field); bad rules quarantined |
| 6 | ATT&CK integration | Navigator export, coverage heatmap |
| 7 | HTTPS/TLS + security hardening | TLS config, security headers, HSTS |
| 8 | Error observability + Serilog | Visible parse errors, app audit logs |
| 9 | FP tuning UI + rule lifecycle | Dismiss/FP markers, staged rule updates |
| 10 | Dashboards (ECharts) | Timeline, distribution charts |
| 11 | Live monitoring (SignalR + EventLogWatcher) | Live tail + browser alerts |
| 12 | Docker packaging + docs | One-command deployment |

> **Security gates (deliberate, not optional):**
> - **Auth is Phase 1, not a later feature.** The API refuses to serve
>   event data until authentication + roles exist. There is never a
>   commit where ingestion/browsing is reachable unauthenticated.
> - **Localhost-only by default.** The server binds to `127.0.0.1` out of
>   the box. Binding to a network interface requires **both** Phase 1
>   (auth) and Phase 7 (TLS) to be complete — a config guard, not a doc note.
> - **Schema is forward-compatible from Phase 2.** Columns the later phases
>   need (`is_false_positive`, dismissal reason, role fields) exist in the
>   initial schema. Later phases fill them in with UI/logic — **no migration
>   onto live data.**

---

## 6. Deferred (Post-MVP)

Deliberately out of the current scope. None of these require a schema change,
so adding them later is additive, not a migration.

| Deferred item | Why it can wait | Note |
|---------------|-----------------|------|
| Data retention (prune by age/size) | Only matters once the store grows; pruning uses the existing timestamp column | Add a maintenance job post-MVP |
| Cold-storage archive | Dev data doesn't need archiving | Export already exists for manual use |
| Scheduled backups (`VACUUM INTO`) | No real case data during development | **Do not store real case data until backups exist** |
| Restore procedure | Follows from backups | Document alongside backups |

**Rule until then:** keep the tool on test/sample logs, bound to `127.0.0.1`.