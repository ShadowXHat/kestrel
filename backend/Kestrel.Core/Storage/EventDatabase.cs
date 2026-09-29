using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kestrel.Core.Storage;

/// <summary>
/// Owns the SQLite event-store file (<c>data/kestrel.db</c>, shared with the
/// identity store but opened through its own connections): connection factory
/// with per-connection pragmas, forward-compatible schema bootstrap and a
/// schema-version guard — see docs/PHASE-2.md.
///
/// Rules honored here (.clinerules):
///   - WAL mode is verified at initialization (not assumed).
///   - Foreign keys are enabled per connection (SQLite does not persist them).
///   - Schema is forward-compatible: FP fields + audit fields exist from day
///     one, and a database written by a NEWER version is refused loudly.
/// </summary>
public sealed class EventDatabase
{
    public const string SchemaVersion = "2";

    private const int BusyTimeoutMilliseconds = 5_000;

    private static readonly string[] SchemaStatements =
    [
        """
        CREATE TABLE IF NOT EXISTS ingest_jobs (
            id                 TEXT PRIMARY KEY,
            file_name          TEXT NOT NULL,
            file_path          TEXT NOT NULL,
            file_size_bytes    INTEGER NOT NULL,
            status             TEXT NOT NULL CHECK (status IN ('queued','running','completed','completed_with_errors','failed','canceled')),
            error              TEXT,
            created_by         TEXT NOT NULL,
            created_at_utc     TEXT NOT NULL,
            started_at_utc     TEXT,
            finished_at_utc    TEXT,
            total_records      INTEGER NOT NULL DEFAULT 0,
            processed_records  INTEGER NOT NULL DEFAULT 0,
            failed_records     INTEGER NOT NULL DEFAULT 0,
            first_errors       TEXT
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS events (
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            job_id            TEXT NOT NULL REFERENCES ingest_jobs(id) ON DELETE CASCADE,
            record_id         INTEGER,
            channel           TEXT NOT NULL,
            event_id          INTEGER NOT NULL,
            provider          TEXT NOT NULL,
            level             INTEGER NOT NULL,
            level_text        TEXT NOT NULL,
            time_created_utc  TEXT NOT NULL,
            computer          TEXT NOT NULL,
            user_sid          TEXT,
            keywords          INTEGER,
            correlation_id    TEXT,
            thread_id         INTEGER,
            process_id        INTEGER,
            xml               TEXT NOT NULL,
            fp_state          INTEGER NOT NULL DEFAULT 0,
            fp_note           TEXT,
            fp_by             TEXT,
            fp_at_utc         TEXT,
            ingested_at_utc   TEXT NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS ix_events_job ON events(job_id)",
        "CREATE INDEX IF NOT EXISTS ix_events_time ON events(time_created_utc)",
        "CREATE INDEX IF NOT EXISTS ix_events_event_id ON events(event_id)",
        "CREATE INDEX IF NOT EXISTS ix_events_channel ON events(channel)",
        "CREATE INDEX IF NOT EXISTS ix_events_fp_state ON events(fp_state)",
        // Composite indexes for the known access patterns:
        //   - channel+event_id: the Sigma matcher's channel+EventID pre-filter (Phase 5)
        //   - channel+time: the Phase 3 browser's per-channel time windows
        "CREATE INDEX IF NOT EXISTS ix_events_channel_event_id ON events(channel, event_id)",
        "CREATE INDEX IF NOT EXISTS ix_events_channel_time ON events(channel, time_created_utc)",
        "CREATE TABLE IF NOT EXISTS schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)",
        // FTS5 full-text search virtual table (Phase 4: SQLite FTS5 search + export)
        "CREATE VIRTUAL TABLE IF NOT EXISTS event_fts USING fts5(channel, provider, level_text, computer, xml, content='events', content_rowid='id')",
        // Keep FTS5 in sync with the events table via triggers
        "CREATE TRIGGER IF NOT EXISTS events_ai AFTER INSERT ON events BEGIN INSERT INTO event_fts(rowid, channel, provider, level_text, computer, xml) VALUES (new.id, new.channel, new.provider, new.level_text, new.computer, new.xml); END",
        "CREATE TRIGGER IF NOT EXISTS events_au AFTER UPDATE ON events BEGIN INSERT INTO event_fts(event_fts, rowid, channel, provider, level_text, computer, xml) VALUES ('delete', old.id, old.channel, old.provider, old.level_text, old.computer, old.xml); INSERT INTO event_fts(rowid, channel, provider, level_text, computer, xml) VALUES (new.id, new.channel, new.provider, new.level_text, new.computer, new.xml); END",
        "CREATE TRIGGER IF NOT EXISTS events_ad AFTER DELETE ON events BEGIN INSERT INTO event_fts(event_fts, rowid, channel, provider, level_text, computer, xml) VALUES ('delete', old.id, old.channel, old.provider, old.level_text, old.computer, old.xml); END"
    ];

    private readonly ILogger<EventDatabase>? _logger;

    public EventDatabase(string dbFilePath, ILogger<EventDatabase>? logger = null)
    {
        DbFilePath = Path.GetFullPath(dbFilePath);
        _logger = logger;
    }

    public string DbFilePath { get; }

    /// <summary>
    /// Opens a fresh connection with the per-connection pragmas applied
    /// (foreign keys, busy timeout). Callers dispose the connection.
    /// </summary>
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DbFilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());

        await connection.OpenAsync(cancellationToken);

        using (var command = connection.CreateCommand())
        {
            // synchronous=NORMAL is the documented pairing for WAL mode: batch
            // commits skip the per-transaction fsync (a real ingest win) while
            // remaining durable across application crashes — only an OS crash/
            // power loss can lose the tail, the trade-off SQLite recommends.
            command.CommandText = $"PRAGMA foreign_keys=ON; PRAGMA busy_timeout={BusyTimeoutMilliseconds}; PRAGMA synchronous=NORMAL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Phase 5: detection SQL relies on the regexp()/xml_field() UDFs, so
        // they are registered on every connection the store hands out.
        SqlFunctions.Register(connection);

        return connection;
    }

    /// <summary>
    /// Creates the schema if missing (idempotent) and guards the schema
    /// version. SQLite DDL is fast; runs once at startup via
    /// <see cref="EventStoreInitializationService"/>.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DbFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);

        // WAL mode is persistent per database file, but verified — not assumed.
        await using (var walCommand = connection.CreateCommand())
        {
            walCommand.CommandText = "PRAGMA journal_mode=WAL;";
            var mode = (await walCommand.ExecuteScalarAsync(cancellationToken))?.ToString();
            if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.LogError("Event store journal_mode is '{Mode}', expected 'wal'.", mode ?? "unknown");
                throw new InvalidOperationException(
                    $"SQLite journal_mode is '{mode ?? "unknown"}'; the event store requires WAL mode (docs/PHASE-2.md).");
            }
        }

        foreach (var statement in SchemaStatements)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = statement;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Forward-compatibility guard: refuse a database written by a newer
        // build instead of silently misreading its schema.
        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.CommandText = "SELECT value FROM schema_meta WHERE key = 'schema_version';";
            var existing = (await versionCommand.ExecuteScalarAsync(cancellationToken))?.ToString();

            if (existing is null)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO schema_meta (key, value) VALUES ('schema_version', $version);";
                insert.Parameters.AddWithValue("$version", SchemaVersion);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (!string.Equals(existing, SchemaVersion, StringComparison.Ordinal))
            {
                _logger?.LogError(
                    "Event database schema version is {Existing}; this build expects {Expected}.",
                    existing, SchemaVersion);
                throw new InvalidOperationException(
                    $"Event database schema version is {existing}; this build expects {SchemaVersion}. " +
                    "The database was written by an incompatible version — refusing to open it (forward-compat guard).");
            }
        }
    }

    /// <summary>
    /// Rebuilds the FTS5 full-text index from the events table.
    /// Use after bulk imports or when the FTS5 index is suspected to be
    /// out of sync with the events table.
    /// </summary>
    public async Task RebuildFts5Async(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO event_fts(event_fts) VALUES ('rebuild');";
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger?.LogInformation("FTS5 index rebuilt.");
    }
}