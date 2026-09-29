namespace Kestrel.Core.Models;

/// <summary>
/// Ingest job lifecycle. Persisted as lowercase text via
/// <see cref="IngestJobStatusText"/> — the same strings form the wire format.
/// </summary>
public enum IngestJobStatus
{
    Queued = 0,
    Running = 1,
    Completed = 2,
    CompletedWithErrors = 3,
    Failed = 4,
    Canceled = 5,
}

/// <summary>
/// Status ⇄ storage/wire text mapping. The database CHECK constraint and the
/// API share these exact strings; unknown values fail loudly, never silently.
/// </summary>
public static class IngestJobStatusText
{
    public static string ToDb(IngestJobStatus status) => status switch
    {
        IngestJobStatus.Queued => "queued",
        IngestJobStatus.Running => "running",
        IngestJobStatus.Completed => "completed",
        IngestJobStatus.CompletedWithErrors => "completed_with_errors",
        IngestJobStatus.Failed => "failed",
        IngestJobStatus.Canceled => "canceled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown ingest job status."),
    };

    public static IngestJobStatus FromDb(string value) => value switch
    {
        "queued" => IngestJobStatus.Queued,
        "running" => IngestJobStatus.Running,
        "completed" => IngestJobStatus.Completed,
        "completed_with_errors" => IngestJobStatus.CompletedWithErrors,
        "failed" => IngestJobStatus.Failed,
        "canceled" => IngestJobStatus.Canceled,
        _ => throw new InvalidOperationException($"Unknown ingest job status in the database: '{value}'."),
    };

    /// <summary>The wire format equals the storage form (one source of truth).</summary>
    public static string ToApi(IngestJobStatus status) => ToDb(status);
}

/// <summary>A single per-record failure, retained (bounded) on the job for diagnostics.</summary>
public sealed record IngestRecordError(long? RecordId, string Reason);

/// <summary>
/// An EVTX ingestion job. The API layer creates it in state <see cref="IngestJobStatus.Queued"/>;
/// the <see cref="Kestrel.Core.Jobs.IngestJobProcessor"/> owns every later transition and
/// persists it through the job store — failures always land in a terminal
/// state with an explicit reason (no silent failures).
/// </summary>
public sealed class IngestJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Display-only file name (sanitized at the upload boundary).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Absolute path of the staged file the ingest service parses.</summary>
    public string FilePath { get; init; } = string.Empty;

    public long FileSizeBytes { get; init; }

    public IngestJobStatus Status { get; set; } = IngestJobStatus.Queued;

    /// <summary>Fatal reason for <see cref="IngestJobStatus.Failed"/>/<see cref="IngestJobStatus.Canceled"/>.</summary>
    public string? Error { get; set; }

    /// <summary>User id of the uploader (audit field, exists from day one).</summary>
    public string CreatedBy { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    /// <summary>Records seen (ingested + skipped).</summary>
    public long TotalRecords { get; set; }

    /// <summary>Records successfully written to the event store.</summary>
    public long ProcessedRecords { get; set; }

    /// <summary>Records skipped due to per-record failures (never silent).</summary>
    public long FailedRecords { get; set; }

    /// <summary>First per-record failures, bounded by IngestOptions.MaxPerRecordErrorsRetained.</summary>
    public IReadOnlyList<IngestRecordError> FirstErrors { get; set; } = [];
}