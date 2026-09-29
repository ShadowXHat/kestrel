using Kestrel.Core.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Kestrel.Tests.Ingest;

public sealed class EventDatabaseTests : IDisposable
{
    private readonly string _directory;
    private readonly string _dbPath;
    private readonly EventDatabase _database;

    public EventDatabaseTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "kestrel-tests", "db-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _dbPath = Path.Combine(_directory, "events.db");
        _database = new EventDatabase(_dbPath);
        _database.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        // Release pooled connections so the WAL files are unlocked on delete.
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private async Task<string> PragmaAsync(string pragma)
    {
        await using var connection = await _database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = pragma;
        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }

    [Fact]
    public async Task Journal_mode_is_wal()
    {
        Assert.Equal("wal", await PragmaAsync("PRAGMA journal_mode;"));
    }

    [Fact]
    public async Task Foreign_keys_are_enabled_per_connection()
    {
        Assert.Equal("1", await PragmaAsync("PRAGMA foreign_keys;"));
    }

    [Fact]
    public async Task Synchronous_mode_is_normal_wal_pairing()
    {
        // synchronous=NORMAL is the documented pairing for WAL mode: batch
        // commits skip the per-transaction fsync while staying durable across
        // application crashes.
        Assert.Equal("1", await PragmaAsync("PRAGMA synchronous;"));
    }

    [Fact]
    public async Task Composite_indexes_for_sigma_prefilter_and_time_browsing_exist()
    {
        await using var connection = await _database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'index'
              AND name IN ('ix_events_channel_event_id', 'ix_events_channel_time');
            """;
        Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Initialize_is_idempotent_and_stamps_schema_version()
    {
        // A second run must not throw (startup calls Initialize on every boot).
        await _database.InitializeAsync();

        await using var connection = await _database.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM schema_meta WHERE key = 'schema_version';";
        Assert.Equal(EventDatabase.SchemaVersion, (await command.ExecuteScalarAsync())?.ToString());
    }

    [Fact]
    public async Task Newer_schema_version_is_refused_loudly()
    {
        {
            await using var connection = await _database.OpenConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_meta SET value = '99' WHERE key = 'schema_version';";
            await command.ExecuteNonQueryAsync();
        }

        var newer = new EventDatabase(_dbPath);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => newer.InitializeAsync());
        Assert.Contains("forward-compat", exception.Message);
    }
}