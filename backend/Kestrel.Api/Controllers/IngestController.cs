using System.Security.Claims;
using Kestrel.Api.Auth;
using Kestrel.Core.Ingest;
using Kestrel.Core.Jobs;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

/// <summary>
/// EVTX ingestion (Phase 2).
///
/// <list type="bullet">
///   <item>Upload requires the Analyst policy (Viewer/Analyst/Admin hierarchy, server-side).</item>
///   <item>The body is the RAW file (application/octet-stream) — streamed to a
///     staged temp file in fixed-size chunks, never buffered into memory
///     (.clinerules: streaming uploads).</item>
///   <item>Magic bytes are validated BEFORE the file is accepted (.clinerules);
///     size is capped by KestrelApp:Ingest:MaxUploadBytes mid-stream.</item>
///   <item>Parsing happens in the background job queue — this endpoint only
///     stages the file and enqueues, returning 202 with a job id.</item>
/// </list>
/// </summary>
[ApiController]
[Route("api/ingest")]
[Produces("application/json")]
public class IngestController : ControllerBase
{
    private readonly IngestJobQueue _queue;
    private readonly IngestJobStore _jobStore;
    private readonly IngestOptions _options;
    private readonly ILogger<IngestController> _logger;

    public IngestController(
        IngestJobQueue queue,
        IngestJobStore jobStore,
        IngestOptions options,
        ILogger<IngestController> logger)
    {
        _queue = queue;
        _jobStore = jobStore;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Streams an EVTX file into the ingest pipeline. The file name travels in
    /// the <c>X-Evtx-Filename</c> header (display only, sanitized). Returns
    /// 202 { jobId, status, jobUrl } — poll the job endpoint for progress.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Policy = Policies.Analyst)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var maxBytes = _options.MaxUploadBytes;
        var fileName = SanitizeFileName(
            Request.Headers["X-Evtx-Filename"].FirstOrDefault() ?? Request.Query["fileName"].FirstOrDefault());

        // Kestrel's default request cap (30 MB) is far below real EVTX sizes.
        // Raise it for this request only; the streaming loop below still
        // aborts anything above the configured cap.
        var bodyFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodyFeature is { IsReadOnly: false })
        {
            bodyFeature.MaxRequestBodySize = maxBytes;
        }

        var declaredLength = Request.ContentLength;
        if (declaredLength is { } size && size > maxBytes)
        {
            _logger.LogWarning(
                "Audit: EVTX upload rejected ({FileName}, {Bytes} bytes) by {Username}: exceeds the configured maximum of {MaxBytes} bytes.",
                fileName, size, User.Identity?.Name, maxBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new
            {
                error = "file_too_large",
                detail = $"Upload exceeds the configured maximum of {maxBytes} bytes (KestrelApp:Ingest:MaxUploadBytes).",
                maxBytes,
            });
        }

        if (declaredLength is { } declared && declared < EvtxFileValidator.MagicLength)
        {
            return BadRequest(new
            {
                error = "not_evtx",
                detail = $"Body is smaller than the EVTX magic prefix ({EvtxFileValidator.MagicLength} bytes) — not an EVTX file.",
            });
        }

        var jobId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(_options.TempDirectory);
        var targetPath = Path.Combine(_options.TempDirectory, $"{jobId}.evtx");
        long written;

        try
        {
            written = await WriteBodyToFileAsync(targetPath, maxBytes, cancellationToken);
        }
        catch (UploadTooLargeException ex)
        {
            TryDelete(targetPath);
            _logger.LogWarning(
                "Audit: EVTX upload aborted ({FileName}) for {Username}: exceeded the configured maximum of {MaxBytes} bytes.",
                fileName, User.Identity?.Name, maxBytes);
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new
            {
                error = "file_too_large",
                detail = ex.Message,
                maxBytes,
            });
        }
        catch (NotEvtxException ex)
        {
            TryDelete(targetPath);
            _logger.LogWarning(
                "Audit: EVTX upload rejected ({FileName}) by {Username}: {Reason}",
                fileName, User.Identity?.Name, ex.Reason);
            return BadRequest(new { error = "not_evtx", detail = ex.Reason });
        }

        var job = new IngestJob
        {
            FileName = fileName,
            FilePath = targetPath,
            FileSizeBytes = written,
            CreatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty,
        };

        await _jobStore.CreateAsync(job, cancellationToken);

        if (!_queue.TryEnqueue(job))
        {
            // This job never reaches the processor (which removes staged files
            // of failed jobs), so cleanup happens here — otherwise every 503
            // leaks the staged file into the staging directory.
            TryDelete(targetPath);

            job.Status = IngestJobStatus.Failed;
            job.Error = $"ingest queue full (capacity {IngestJobQueue.Capacity})";
            job.FinishedAtUtc = DateTimeOffset.UtcNow;
            await _jobStore.SetTerminalAsync(job, cancellationToken);
            _logger.LogWarning(
                "Audit: EVTX upload {JobId} ({FileName}) rejected — ingest queue full (capacity {Capacity}).",
                job.Id, fileName, IngestJobQueue.Capacity);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "queue_full",
                detail = "The ingest queue is full; try again shortly.",
            });
        }

        _logger.LogInformation(
            "Audit: EVTX upload accepted {JobId} ({FileName}, {Bytes} bytes) by {Username}.",
            job.Id, fileName, written, User.Identity?.Name);

        return Accepted(new { jobId = job.Id, status = "queued", jobUrl = $"/api/ingest/jobs/{job.Id}" });
    }

    /// <summary>Recent ingest jobs, newest first (Viewer+ — status visibility, no event data).</summary>
    [HttpGet("jobs")]
    [Authorize(Policy = Policies.Viewer)]
    public async Task<IActionResult> List(
        [FromQuery(Name = "limit")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);
        var jobs = await _jobStore.ListRecentAsync(limit, cancellationToken);
        return Ok(jobs.Select(job => job.ToDto()).ToArray());
    }

    /// <summary>One ingest job incl. progress counters and retained per-record errors.</summary>
    [HttpGet("jobs/{id}")]
    [Authorize(Policy = Policies.Viewer)]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        var job = await _jobStore.GetAsync(id, cancellationToken);
        if (job is null)
        {
            return NotFound(new { error = "job_not_found" });
        }

        return Ok(job.ToDto());
    }

    /// <summary>
    /// Streams the request body to the staged file: validates the EVTX magic
    /// bytes from the first 8 bytes BEFORE committing disk work, then copies
    /// fixed-size chunks while enforcing the byte cap — the body is never
    /// buffered into memory (.clinerules).
    /// </summary>
    private async Task<long> WriteBodyToFileAsync(string targetPath, long maxBytes, CancellationToken cancellationToken)
    {
        await using (var target = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            // 1. Read the magic prefix first — reject junk before any large write.
            var header = new byte[EvtxFileValidator.MagicLength];
            var headerRead = 0;
            while (headerRead < header.Length)
            {
                var read = await Request.Body.ReadAsync(header.AsMemory(headerRead), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                headerRead += read;
            }

            var verdict = EvtxFileValidator.ValidateHeader(header.AsSpan(0, headerRead), declaredSize: null, maxBytes);
            if (!verdict.IsValid)
            {
                throw new NotEvtxException(verdict.Reason ?? "not an EVTX file");
            }

            await target.WriteAsync(header.AsMemory(0, headerRead), cancellationToken);
            var written = (long)headerRead;

            // 2. Stream the remainder in fixed-size chunks; enforce the cap mid-stream.
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await Request.Body.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                if (written + read > maxBytes)
                {
                    throw new UploadTooLargeException(
                        $"Upload exceeds the configured maximum of {maxBytes} bytes (KestrelApp:Ingest:MaxUploadBytes).");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
            }

            return written;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            System.IO.File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete the staged upload file {Path}.", path);
        }
    }

    /// <summary>Display-only name: strips any path segments, caps length.</summary>
    private static string SanitizeFileName(string? raw)
    {
        var name = string.IsNullOrWhiteSpace(raw) ? "upload.evtx" : raw!;
        name = Path.GetFileName(name.Replace('\\', '/').Trim());
        if (name.Length > 128)
        {
            name = name[..128];
        }

        return string.IsNullOrWhiteSpace(name) ? "upload.evtx" : name;
    }

    /// <summary>Thrown when the streamed body exceeds the configured cap.</summary>
    private sealed class UploadTooLargeException : Exception
    {
        public UploadTooLargeException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Thrown when the streamed body fails EVTX validation.</summary>
    private sealed class NotEvtxException : Exception
    {
        public string Reason { get; }

        public NotEvtxException(string reason)
            : base(reason)
        {
            Reason = reason;
        }
    }
}