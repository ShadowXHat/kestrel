using System.Globalization;
using System.Text.Json;
using Kestrel.Core.Models;
using Microsoft.Data.Sqlite;

namespace Kestrel.Core.Storage;

/// <summary>
/// Persistence for <see cref="IngestJob"/> lifecycle rows. Timestamps are
/// stored as ISO-8601 (round-trip, UTC) so SQLite's lexicographic TEXT
/// ordering equals chronological ordering; statuses use the shared lowercase
/// strings of <see cref="IngestJobStatusText"/>.
/// </summary>
public sealed class IngestJobStore
{
    private const string SelectColumns = """
        id, file_name, file_path, file_size_bytes, status, error, created_by,
        created_at_utc, started_at_utc, finished_at_utc,
        total_records, processed_records, failed_records, first_errors
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

    private readonly EventDatabase _database;

    public IngestJobStore(EventDatabase database) => _database = database;

    public async Task CreateAsync(IngestJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO ingest_jobs (
                id, file_name, file_path, file_size_bytes, status, error, created_by,
                created_at_utc, started_at_utc, finished_at_utc,
                total_records, processed_records, failed_records, first_errors)
            VALUES (
                $id, $file_name, $file_path, $file_size_bytes, $status, $error, $created_by,
                $created_at_utc, $started_at_utc, $finished_at_utc,
                $total_records, $processed_records, $failed_records, $first_errors);
            """;

        command.Parameters.AddWithValue("$id", job.Id);
        command.Parameters.AddWithValue("$file_name", job.FileName);
        command.Parameters.AddWithValue("$file_path", job.FilePath);
        command.Parameters.AddWithValue("$file_size_bytes", job.FileSizeBytes);
        command.Parameters.AddWithValue("$status", IngestJobStatusText.ToDb(job.Status));
        command.Parameters.AddWithValue("$error", Db(job.Error));
        command.Parameters.AddWithValue("$created_by", job.CreatedBy);
        command.Parameters.AddWithValue("$created_at_utc", ToTimestamp(job.CreatedAtUtc));
        command.Parameters.AddWithValue("$started_at_utc", Db(ToTimestamp(job.StartedAtUtc)));
        command.Parameters.AddWithValue("$finished_at_utc", Db(ToTimestamp(job.FinishedAtUtc)));
        command.Parameters.AddWithValue("$total_records", job.TotalRecords);
        command.Parameters.AddWithValue("$processed_records", job.ProcessedRecords);
        command.Parameters.AddWithValue("$failed_records", job.FailedRecords);
        command.Parameters.AddWithValue("$first_errors", Db(SerializeFirstErrors(job.FirstErrors)));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IngestJob?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM ingest_jobs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapJob(reader);
    }

    public async Task<IReadOnlyList<IngestJob>> ListRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM ingest_jobs ORDER BY created_at_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var jobs = new List<IngestJob>();
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(MapJob(reader));
        }

        return jobs;
    }

    /// <summary>
    /// Lists jobs currently in the given states, oldest first — used by the
    /// processor's startup recovery to re-drive jobs orphaned by a process
    /// restart (the in-memory queue loses them; the rows survive).
    /// </summary>
    public async Task<IReadOnlyList<IngestJob>> ListByStatusAsync(
        IReadOnlyList<IngestJobStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        if (statuses.Count == 0)
        {
            return [];
        }

        var parameterNames = new string[statuses.Count];
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        for (var i = 0; i < statuses.Count; i++)
        {
            parameterNames[i] = $"$status{i}";
            command.Parameters.AddWithValue(parameterNames[i], IngestJobStatusText.ToDb(statuses[i]));
        }

        command.CommandText = $"SELECT {SelectColumns} FROM ingest_jobs " +
                              $"WHERE status IN ({string.Join(", ", parameterNames)}) ORDER BY created_at_utc ASC;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var jobs = new List<IngestJob>();
        while (await reader.ReadAsync(cancellationToken))
        {
            jobs.Add(MapJob(reader));
        }

        return jobs;
    }

    public async Task SetRunningAsync(string jobId, DateTimeOffset startedAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ingest_jobs SET status = $status, started_at_utc = $started WHERE id = $id;";
        command.Parameters.AddWithValue("$status", IngestJobStatusText.ToDb(IngestJobStatus.Running));
        command.Parameters.AddWithValue("$started", ToTimestamp(startedAtUtc));
        command.Parameters.AddWithValue("$id", jobId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Progress checkpoint: counters + bounded first-error list.</summary>
    public async Task SetCountersAsync(IngestJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_jobs
            SET total_records = $total,
                processed_records = $processed,
                failed_records = $failed,
                first_errors = $first_errors
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue("$total", job.TotalRecords);
        command.Parameters.AddWithValue("$processed", job.ProcessedRecords);
        command.Parameters.AddWithValue("$failed", job.FailedRecords);
        command.Parameters.AddWithValue("$first_errors", Db(SerializeFirstErrors(job.FirstErrors)));
        command.Parameters.AddWithValue("$id", job.Id);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Persists a terminal transition (completed / completed_with_errors /
    /// failed / canceled) together with the final counters.
    /// </summary>
    public async Task SetTerminalAsync(IngestJob job, CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE ingest_jobs
            SET status = $status,
                error = $error,
                finished_at_utc = $finished,
                total_records = $total,
                processed_records = $processed,
                failed_records = $failed,
                first_errors = $first_errors
            WHERE id = $id;
            """;

        command.Parameters.AddWithValue("$status", IngestJobStatusText.ToDb(job.Status));
        command.Parameters.AddWithValue("$error", Db(job.Error));
        command.Parameters.AddWithValue("$finished", Db(ToTimestamp(job.FinishedAtUtc)));
        command.Parameters.AddWithValue("$total", job.TotalRecords);
        command.Parameters.AddWithValue("$processed", job.ProcessedRecords);
        command.Parameters.AddWithValue("$failed", job.FailedRecords);
        command.Parameters.AddWithValue("$first_errors", Db(SerializeFirstErrors(job.FirstErrors)));
        command.Parameters.AddWithValue("$id", job.Id);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IngestJob MapJob(SqliteDataReader reader)
    {
        return new IngestJob
        {
            Id = reader.GetString(0),
            FileName = reader.GetString(1),
            FilePath = reader.GetString(2),
            FileSizeBytes = reader.GetInt64(3),
            Status = IngestJobStatusText.FromDb(reader.GetString(4)),
            Error = reader.IsDBNull(5) ? null : reader.GetString(5),
            CreatedBy = reader.GetString(6),
            CreatedAtUtc = ParseTimestamp(reader.GetString(7)) ?? DateTimeOffset.MinValue,
            StartedAtUtc = ParseTimestamp(reader.GetString(8)),
            FinishedAtUtc = ParseTimestamp(reader.GetString(9)),
            TotalRecords = reader.GetInt64(10),
            ProcessedRecords = reader.GetInt64(11),
            FailedRecords = reader.GetInt64(12),
            FirstErrors = reader.IsDBNull(13)
                ? []
                : JsonSerializer.Deserialize<List<IngestRecordError>>(reader.GetString(13), JsonOptions) ?? [],
        };
    }

    private static string? SerializeFirstErrors(IReadOnlyList<IngestRecordError> firstErrors) =>
        firstErrors.Count == 0 ? null : JsonSerializer.Serialize(firstErrors, JsonOptions);

    private static object Db(object? value) => value ?? DBNull.Value;

    private static string? ToTimestamp(DateTimeOffset? value) => value?.ToString("o", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        string.IsNullOrEmpty(value)
            ? null
            : DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}