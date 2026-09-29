using Kestrel.Core.Models;

namespace Kestrel.Api.Controllers;

/// <summary>Wire representation of an ingest job (REST responses and SignalR push).</summary>
public sealed record IngestJobDto(
    string Id,
    string FileName,
    long FileSizeBytes,
    string Status,
    string? Error,
    string CreatedBy,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long TotalRecords,
    long ProcessedRecords,
    long FailedRecords,
    IReadOnlyList<IngestRecordError> FirstErrors);

public static class IngestJobMappings
{
    public static IngestJobDto ToDto(this IngestJob job) => new(
        job.Id,
        job.FileName,
        job.FileSizeBytes,
        IngestJobStatusText.ToApi(job.Status),
        job.Error,
        job.CreatedBy,
        job.CreatedAtUtc,
        job.StartedAtUtc,
        job.FinishedAtUtc,
        job.TotalRecords,
        job.ProcessedRecords,
        job.FailedRecords,
        job.FirstErrors);
}