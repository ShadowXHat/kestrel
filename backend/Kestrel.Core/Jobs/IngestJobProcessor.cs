using Kestrel.Core.Ingest;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kestrel.Core.Jobs;

/// <summary>
/// Single-consumer background processor for the ingest job queue. One job at a
/// time (SQLite is a single-writer store). Every job gets a hard timeout from
/// <see cref="IngestOptions.JobTimeout"/> and ALWAYS lands in a terminal state
/// with an explicit reason — failures are never silent (.clinerules).
/// </summary>
public sealed class IngestJobProcessor : BackgroundService
{
    private readonly IngestJobQueue _queue;
    private readonly IngestJobStore _jobStore;
    private readonly IEvtxIngestService _ingestService;
    private readonly IngestOptions _options;
    private readonly IIngestProgressSink _progressSink;
    private readonly ILogger<IngestJobProcessor> _logger;

    public IngestJobProcessor(
        IngestJobQueue queue,
        IngestJobStore jobStore,
        IEvtxIngestService ingestService,
        IngestOptions options,
        IIngestProgressSink progressSink,
        ILogger<IngestJobProcessor> logger)
    {
        _queue = queue;
        _jobStore = jobStore;
        _ingestService = ingestService;
        _options = options;
        _progressSink = progressSink;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Ingest job processor started (timeout {TimeoutSeconds:0}s, batch {BatchSize}, record cap {MaxRecordsPerJob}).",
            _options.JobTimeout.TotalSeconds, _options.BatchSize, _options.MaxRecordsPerJob);

        try
        {
            // Jobs persisted in a non-terminal state by a previous process
            // would otherwise stay there forever (the queue is in-memory) —
            // recover them before consuming the queue.
            await RecoverOrphanedJobsAsync(stoppingToken);

            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
            {
                await ProcessJobAsync(job, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Ingest job processor stopped.");
        }
    }

    /// <summary>
    /// Startup recovery for jobs orphaned by a process restart (the queue is
    /// in-memory, so their rows would stay 'queued'/'running' forever — no
    /// silent failures, .clinerules). Queued jobs whose staged file survived
    /// are re-enqueued (oldest first); everything else lands in 'failed' with
    /// an explicit reason and its staged file is removed.
    /// </summary>
    private async Task RecoverOrphanedJobsAsync(CancellationToken cancellationToken)
    {
        var orphans = await _jobStore.ListByStatusAsync(
            [IngestJobStatus.Queued, IngestJobStatus.Running], cancellationToken);
        if (orphans.Count == 0)
        {
            return;
        }

        _logger.LogWarning(
            "Ingest processor found {OrphanCount} ingest job(s) in a non-terminal state from a previous run — recovering.",
            orphans.Count);

        foreach (var job in orphans)
        {
            if (job.Status == IngestJobStatus.Queued && File.Exists(job.FilePath) && _queue.TryEnqueue(job))
            {
                _logger.LogInformation(
                    "Ingest job {JobId} ({FileName}) was queued when the previous process stopped — re-queued after restart.",
                    job.Id, job.FileName);
                continue;
            }

            var reason = job.Status == IngestJobStatus.Queued
                ? File.Exists(job.FilePath)
                    ? $"the staged file survived the restart but the ingest queue was full (capacity {IngestJobQueue.Capacity}); re-upload the file"
                    : "the staged file was lost while the application was stopped; re-upload the file"
                : "interrupted by an application restart while running; re-upload the file";

            job.Status = IngestJobStatus.Failed;
            job.Error = reason;
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            await PersistTerminalAsync(job, new InvalidOperationException(reason));
            TryDeleteStagedFile(job);
            await PublishProgressAsync(job);
        }
    }

    private async Task ProcessJobAsync(IngestJob job, CancellationToken shutdownToken)
    {
        using var jobTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        jobTimeout.CancelAfter(_options.JobTimeout);
        var jobToken = jobTimeout.Token;

        job.Status = IngestJobStatus.Running;
        job.StartedAtUtc = DateTimeOffset.UtcNow;
        await _jobStore.SetRunningAsync(job.Id, job.StartedAtUtc.Value, shutdownToken);
        _logger.LogInformation(
            "Ingest job {JobId} started: {FileName} ({FileSizeBytes} bytes, created by {CreatedBy}).",
            job.Id, job.FileName, job.FileSizeBytes, job.CreatedBy);

        try
        {
            await _ingestService.IngestAsync(job, jobToken);

            job.Status = job.FailedRecords > 0 ? IngestJobStatus.CompletedWithErrors : IngestJobStatus.Completed;
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            await _jobStore.SetTerminalAsync(job, shutdownToken);
            _logger.LogInformation(
                "Ingest job {JobId} {Status}: {Processed}/{Total} records ingested, {Failed} failed.",
                job.Id, IngestJobStatusText.ToApi(job.Status), job.ProcessedRecords, job.TotalRecords, job.FailedRecords);
        }
        catch (OperationCanceledException) when (jobToken.IsCancellationRequested && !shutdownToken.IsCancellationRequested)
        {
            await MarkCanceledAsync(job, $"timed out after {_options.JobTimeout.TotalSeconds:0} seconds (KestrelApp:Ingest:JobTimeoutMinutes).", shutdownToken);
        }
        catch (OperationCanceledException)
        {
            await MarkCanceledAsync(job, "canceled during application shutdown", shutdownToken);
        }
        catch (Exception ex)
        {
            job.Status = IngestJobStatus.Failed;
            job.Error = $"{ex.GetType().Name}: {ex.Message}";
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            await PersistTerminalAsync(job, ex);
        }

        // Staged files of failed/canceled jobs are garbage; keep completed
        // ones for later re-analysis (retention lifecycle arrives in Phase 9).
        if (job.Status is IngestJobStatus.Failed or IngestJobStatus.Canceled)
        {
            TryDeleteStagedFile(job);
        }

        await PublishProgressAsync(job);
    }

    private async Task MarkCanceledAsync(IngestJob job, string reason, CancellationToken shutdownToken)
    {
        job.Status = IngestJobStatus.Canceled;
        job.Error = reason;
        job.FinishedAtUtc = DateTimeOffset.UtcNow;

        try
        {
            await _jobStore.SetTerminalAsync(job, shutdownToken);
        }
        catch (Exception persistEx)
        {
            _logger.LogError(
                persistEx,
                "Ingest job {JobId} was canceled ({Reason}) but its terminal state could not be persisted.",
                job.Id, reason);
        }

        _logger.LogWarning("Ingest job {JobId} canceled: {Reason}.", job.Id, reason);
    }

    private async Task PersistTerminalAsync(IngestJob job, Exception cause)
    {
        try
        {
            await _jobStore.SetTerminalAsync(job, CancellationToken.None);
            _logger.LogError(cause, "Ingest job {JobId} failed: {Error}", job.Id, job.Error);
        }
        catch (Exception persistEx)
        {
            _logger.LogError(
                persistEx,
                "Ingest job {JobId} failed with '{Error}' and persisting the terminal state also failed.",
                job.Id, job.Error);
        }
    }

    private void TryDeleteStagedFile(IngestJob job)
    {
        try
        {
            File.Delete(job.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Ingest job {JobId}: could not delete the staged file {FilePath}.",
                job.Id, job.FilePath);
        }
    }

    private async Task PublishProgressAsync(IngestJob job)
    {
        try
        {
            await _progressSink.OnProgressAsync(job);
        }
        catch (Exception sinkEx)
        {
            _logger.LogWarning(sinkEx, "Ingest progress sink failed for job {JobId}.", job.Id);
        }
    }
}