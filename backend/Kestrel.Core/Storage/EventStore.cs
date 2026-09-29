using System.Globalization;
using Kestrel.Core.Models;
using Microsoft.Data.Sqlite;

namespace Kestrel.Core.Storage;

/// <summary>
/// Writes and counts event rows. Inserts happen in explicit transactions,
/// one parameterized command reused per row — the batch size comes from
/// <see cref="Ingest.IngestOptions.BatchSize"/> and bounds ingest memory.
/// </summary>
public sealed class EventStore
{
    private const string InsertSql = """
        INSERT INTO events (
            job_id, record_id, channel, event_id, provider, level, level_text,
            time_created_utc, computer, user_sid, keywords, correlation_id,
            thread_id, process_id, xml, fp_state, ingested_at_utc)
        VALUES (
            $job_id, $record_id, $channel, $event_id, $provider, $level, $level_text,
            $time_created_utc, $computer, $user_sid, $keywords, $correlation_id,
            $thread_id, $process_id, $xml, 0, $ingested_at_utc);
        """;

    private static readonly string[] InsertParameterNames =
    [
        "@job_id", "@record_id", "@channel", "@event_id", "@provider", "@level", "@level_text",
        "@time_created_utc", "@computer", "@user_sid", "@keywords", "@correlation_id",
        "@thread_id", "@process_id", "@xml", "@ingested_at_utc",
    ];

    private readonly EventDatabase _database;

    public EventStore(EventDatabase database) => _database = database;

    /// <summary>
    /// Inserts a batch in one transaction. On any failure the transaction is
    /// rolled back by connection disposal and the exception propagates to the
    /// ingest service — never a silent partial write.
    /// </summary>
    public async Task InsertBatchAsync(string jobId, IReadOnlyList<StoredEvent> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        using var transaction = (SqliteTransaction)connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = InsertSql;

        var parameters = new Dictionary<string, SqliteParameter>(InsertParameterNames.Length);
        foreach (var name in InsertParameterNames)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            command.Parameters.Add(parameter);
            parameters[name] = (SqliteParameter)parameter;
        }

        foreach (var e in events)
        {
            parameters["@job_id"].Value = jobId;
            parameters["@record_id"].Value = Db(e.RecordId);
            parameters["@channel"].Value = e.Channel;
            parameters["@event_id"].Value = e.EventId;
            parameters["@provider"].Value = e.Provider;
            parameters["@level"].Value = e.Level;
            parameters["@level_text"].Value = e.LevelText;
            parameters["@time_created_utc"].Value = ToTimestamp(e.TimeCreatedUtc);
            parameters["@computer"].Value = e.Computer;
            parameters["@user_sid"].Value = Db(e.UserSid);
            parameters["@keywords"].Value = Db(e.Keywords);
            parameters["@correlation_id"].Value = Db(e.CorrelationId);
            parameters["@thread_id"].Value = Db(e.ThreadId);
            parameters["@process_id"].Value = Db(e.ProcessId);
            parameters["@xml"].Value = e.Xml;
            parameters["@ingested_at_utc"].Value = ToTimestamp(e.IngestedAtUtc);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    public async Task<long> CountByJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM events WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string ToTimestamp(DateTimeOffset value) => value.ToString("o", CultureInfo.InvariantCulture);
}