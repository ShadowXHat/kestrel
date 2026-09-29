using System.Text;
using Kestrel.Api.Auth;
using Kestrel.Core.Export;
using Kestrel.Core.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

[ApiController]
[Route("api/export")]
[Authorize(Policy = Policies.Analyst)]
[Produces("application/json")]
public class ExportController : ControllerBase
{
    private readonly ExportService _export;
    private readonly ILogger<ExportController> _logger;

    public ExportController(ExportService export, ILogger<ExportController> logger)
    {
        _export = export;
        _logger = logger;
    }

    /// <summary>
    /// Export events to CSV, JSON, or JSONL format (Phase 4).
    /// Returns a downloadable file. Supports FTS5 queries and filters.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Export([FromBody] ExportRequest request, CancellationToken cancellationToken = default)
    {
        var format = request.Format?.ToLowerInvariant() ?? "csv";
        if (format is not "csv" and not "json" and not "jsonl")
        {
            return BadRequest(new { error = "invalid_format", detail = "Format must be 'csv', 'json', or 'jsonl'." });
        }

        var pageSize = Math.Clamp(request.PageSize ?? 10000, 1, 100000);
        var skip = Math.Max(0, request.Skip ?? 0);

        var query = new ExportQuery(
            FtsQuery: request.Query,
            Channel: request.Channel,
            Level: request.Level,
            Computer: request.Computer,
            Provider: request.Provider,
            TimeStart: request.TimeStart,
            TimeEnd: request.TimeEnd,
            Format: format,
            PageSize: pageSize,
            Skip: skip,
            Descending: request.Descending ?? true);

        var count = await _export.CountAsync(query, cancellationToken);
        var result = await _export.ExportAsync(query, cancellationToken);

        _logger.LogInformation(
            "Audit: Export {Format} of {Count} events requested by {Username}.",
            format, count, User.Identity?.Name);

        return File(Encoding.UTF8.GetBytes(result.Content), result.ContentType, result.FileName);
    }
}

public sealed class ExportRequest
{
    public string? Query { get; set; }
    public string? Channel { get; set; }
    public byte? Level { get; set; }
    public string? Computer { get; set; }
    public string? Provider { get; set; }
    public DateTimeOffset? TimeStart { get; set; }
    public DateTimeOffset? TimeEnd { get; set; }
    public string? Format { get; set; }
    public int? PageSize { get; set; }
    public int? Skip { get; set; }
    public bool? Descending { get; set; }
}
