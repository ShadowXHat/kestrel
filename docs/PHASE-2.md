# Phase 2 — Forward-Compatible SQLite Schema + EVTX Ingest

> Status: **implemented**. Scope per `AGENTS.md` phase 2: *"Forward-compatible
> SQLite schema + EVTX ingest."* Build on the Phase 1 auth gate: every Phase 2
> endpoint is authenticated, and upload additionally requires the **Analyst**
> policy (server-side role hierarchy).

## What ships in Phase 2

| Area | Implementation |
|------|----------------|
| Event store | `Kestrel.Core` SQLite via Microsoft.Data.Sqlite — same `data/kestrel.db` file as Identity, own connection pool, **WAL verified**, foreign keys per connection, busy timeout |
| Forward-compatible schema | `events` + `ingest_jobs` + `schema_meta` (version `2`); FP fields (`fp_state`/`fp_note`/`fp_by`/`fp_at_utc`) and audit fields exist from day one |
| Schema guard | A database written by a NEWER build is refused loudly at startup (forward-compat guard), never silently misread |
| EVTX ingest | `System.Diagnostics.Eventing.Reader` (native Windows API) — streaming read, raw event XML stored verbatim, **no message rendering in the hot path** |
| Magic bytes | `ElfFile\0` checked from the first 8 bytes of the upload BEFORE disk work; rejected junk costs nothing |
| Streaming upload | Raw body (`application/octet-stream`) staged in 64 KiB chunks — never buffered into memory; byte cap enforced mid-stream |
| Background jobs | Bounded channel queue (16) + single-consumer `IngestJobProcessor` with per-job timeout, record cap, bounded per-record error retention |
| No silent failures | Per-record problems are counted + retained (bounded) + logged; fatal problems land the job in `failed`/`canceled` with an explicit reason |
| Progress | SignalR push on `EventsHub` group `ingest-jobs` (`IngestProgress` messages, job status only — never event data) |
| Audit trail | Serilog structured logs for every upload accept/reject/abort and every job lifecycle transition |
| Restart recovery | Jobs left in a non-terminal state (`queued`/`running`) by a previous process are recovered at processor startup: queued jobs with an intact staged file are re-enqueued (oldest first); the rest land in `failed` with an explicit reason and their staged file is removed |
| Queue-full hygiene | A `503 queue_full` also deletes the staged file — that job never reaches the processor (which cleans up failed jobs), so cleanup happens at the upload boundary |
| WAL durability pairing | Event-store connections run `PRAGMA synchronous=NORMAL` (the documented pairing for WAL mode): batch commits skip the per-transaction fsync while remaining durable across application crashes |
| Query-path indexes | Composite `ix_events_channel_event_id` (the Sigma matcher's channel+EventID pre-filter, Phase 5) and `ix_events_channel_time` (per-channel time windows, Phase 3) exist from day one |
| Per-record read failures | A record that cannot be READ mid-stream (corrupt record) is skipped, counted, retained and logged like any other per-record failure; only a failure on the first read (or 100 consecutive unreadable records) is file-level fatal |

## Endpoints

| Method | Route | Auth | Notes |
|--------|-------|------|-------|
| POST | `/api/ingest/upload` | **Analyst+** | Body = raw EVTX (`application/octet-stream`); file name in `X-Evtx-Filename` header (display only). `202 { jobId, status: "queued", jobUrl }`. `400 not_evtx` (magic), `413 file_too_large`, `503 queue_full` |
| GET | `/api/ingest/jobs?limit=50` | Viewer+ | Recent jobs, newest first (limit clamped 1–200) |
| GET | `/api/ingest/jobs/{id}` | Viewer+ | Job detail: status, counters, retained per-record errors |
| GET | `/api/events` | Viewer+ | Still 501 — the query surface (browser, streaming rows) is Phase 3 |

## Configuration (`KestrelApp:Ingest`)

| Key | Default | Meaning |
|-----|---------|---------|
| `MaxUploadBytes` | `2147483648` | Hard cap per upload (2 GiB); oversized bodies are refused with `413` before/mid stream |
| `JobTimeoutMinutes` | `30` | Per-job hard timeout; the processor cancels the job beyond it (`canceled`, reason "timed out") |
| `BatchSize` | `1000` | Events per transactional batch flush (bounds ingest memory) |
| `MaxRecordsPerJob` | `5000000` | Safety valve; a job stops with a reason when the cap is hit |
| `MaxPerRecordErrorsRetained` | `50` | Per-record failures retained on the job for diagnostics |
| `TempDirectory` | `data/uploads-tmp` | Staging dir (content-root relative; absolutized at startup) |
| `ConnectionStrings:EventsConnection` | `Data Source=data/kestrel.db` | Event store connection (absolutized like the identity store) |

Nonsense configuration values fail startup with an explicit error — never a
silent fallback.

## Schema (version 2)

`ingest_jobs` — one row per upload; lifecycle `queued → running →
{completed, completed_with_errors, failed, canceled}` (CHECK-constrained
lowercase strings, also the wire format). Counters (`total_records`,
`processed_records`, `failed_records`) plus `first_errors` (JSON, bounded)
persist per-record failures for diagnosis. `created_by` records the uploader
(audit field from day one).

`events` — normalized rows with forward-compatible columns from day one:
`record_id`, `channel`, `event_id`, `provider`, `level`, `level_text`,
`time_created_utc` (ISO-8601 UTC text — lexicographic = chronological),
`computer`, `user_sid`, `keywords`, `correlation_id`, `thread_id`,
`process_id`, **`xml` (raw event XML — messages render lazily in Phase 3+)**,
**`fp_state`/`fp_note`/`fp_by`/`fp_at_utc` (false-positive tuning, Phase 9)**,
`ingested_at_utc`. Indexed on `job_id`, `time_created_utc`, `event_id`,
`channel`, `fp_state`, plus the composites `(channel, event_id)` and
`(channel, time_created_utc)`. FTS5 arrives in Phase 4 — the `xml` column is its
external-content source, so nothing needs to be re-ingested.

## Failure semantics (deliberate)

- **Per-record:** a record that cannot be normalized is skipped, counted
  (`failed_records`), logged, and (up to the retained budget) stored on the
  job with a reason. The job continues.
- **Per-job fatal:** unreadable/corrupt file, timeout, record cap, or a write
  failure → `failed` (or `canceled` for timeout/shutdown) with `error` set;
  staged files of failed/canceled jobs are deleted, completed ones are kept
  for later re-analysis (retention lifecycle is Phase 9).
- **Queue full:** `503 queue_full` — the upload is refused, the job row is
  marked failed with the reason, and the staged file is deleted; nothing is
  silently buffered.
- **Schema version mismatch:** startup refuses the database loudly.

## Windows-only note

EVTX parsing uses `System.Diagnostics.Eventing.Reader` (the native Windows
API, AGENTS.md decision #4). All three backend assemblies are annotated
`[assembly: SupportedOSPlatform("windows")]` — running the parser on a
non-Windows host surfaces an explicit `PlatformNotSupportedException` that
fails the ingest job with a reason (still no silent failures).

## Known limitations

- `EventLogReader.ReadEvent()` is a synchronous native call: the per-job
  timeout and the cancellation token take effect **between** records, not
  inside a blocked read. A read that never returns still lands the job in a
  terminal state (timeout), but only after the blocked call itself completes.
- Per-record error budgets (`first_errors`) are bounded by configuration;
  beyond the budget failures are counted and logged but not stored on the job.

## Running

```bash
cd backend
dotnet restore
dotnet build
dotnet run --project Kestrel.Api/Kestrel.Api.csproj   # binds http://127.0.0.1:5000
```

Login (dev bootstrap `admin` / `Dev#Admin-2026!` — see docs/PHASE-1.md), then:

```bash
curl -b cookies.txt -X POST http://127.0.0.1:5000/api/ingest/upload \
     -H "X-Evtx-Filename: lab.evtx" -H "Content-Type: application/octet-stream" \
     --data-binary @C:\Windows\System32\winevt\Logs\Security.evtx
```

Poll `GET /api/ingest/jobs/{jobId}` for progress; join the `ingest-jobs`
SignalR group for live `IngestProgress` pushes.

## Tests

```bash
cd backend
dotnet test
```

Phase 2 adds xUnit coverage for: the EVTX magic validator (accept/reject
matrix), schema bootstrap (WAL verified, `synchronous=NORMAL`, idempotent,
version guard, composite indexes present), the event/job stores (insert +
counters + lifecycle round trip, status-list query), the job processor state
machine (completed / completed_with_errors / failed / timeout-canceled, queue
handoff, startup recovery of restart-orphaned jobs — re-queue when the staged
file survived, fail loudly otherwise), and hosted API integration: 401 without a
session, 403 for Viewer, 400 on wrong magic, 413 above the cap, and the full
pipeline (202 → background processor → failed job with an explicit reason
for a magic-valid junk file).

## Deliberate scope boundaries (what Phase 2 does NOT do)

- **FTS5 search** is Phase 4 (schema stays external-content ready).
- **Query surface / event browser** is Phase 3 (`GET /api/events` stays 501).
- **Sigma matching** is Phase 5 (no rule fields are populated yet; the
  forward-compatible columns are reserved).
- **Retention/cleanup of completed staged files** is Phase 9.
- **EF migrations** for the identity store are still pending (see
  docs/PHASE-1.md); the event store uses its own idempotent DDL bootstrap.

## Files

```
backend/Kestrel.Core/
├── Models/
│   ├── IngestJob.cs                      # job model + status enum ⇄ text
│   └── StoredEvent.cs                    # event row model + EventLevels
├── Storage/
│   ├── EventDatabase.cs                  # connection factory, WAL, DDL, version guard
│   ├── EventStore.cs                     # batched transactional inserts + counts
│   ├── IngestJobStore.cs                 # job lifecycle persistence
│   └── EventStoreInitializationService.cs# hosted schema bootstrap
├── Ingest/
│   ├── IngestOptions.cs                  # validated configuration
│   ├── EvtxFileValidator.cs              # magic-byte gate
│   ├── IEvtxIngestService.cs             # ingest abstraction (test seam)
│   ├── RecordSkippedException.cs         # per-record skip semantics
│   └── EvtxIngestService.cs              # Eventing.Reader streaming ingest
└── Jobs/
    ├── IngestJobQueue.cs                 # bounded channel (16)
    ├── IIngestProgressSink.cs            # progress abstraction (+ no-op)
    └── IngestJobProcessor.cs             # single-consumer BackgroundService
backend/Kestrel.Api/
├── Controllers/
│   ├── IngestController.cs               # streaming upload + job status
│   └── IngestMappings.cs                 # wire DTO
└── Hubs/
    └── EventsHubIngestProgressSink.cs    # SignalR progress push
```
