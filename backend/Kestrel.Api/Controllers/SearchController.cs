using Kestrel.Api.Auth;
using Kestrel.Core.Search;
using Kestrel.Core.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

[ApiController]
[Route("api/search")]
[Authorize(Policy = Policies.Viewer)]
[Produces("application/json")]
public class SearchController : ControllerBase
{
    private readonly SearchService _search;
    private readonly ILogger<SearchController> _logger;

    public SearchController(SearchService search, ILogger<SearchController> logger)
    {
        _search = search;
        _logger = logger;
    }

    /// <summary>
    /// Full-text search across events using SQLite FTS5 (Phase 4).
    /// Supports keyword queries, channel/level/computer filters,
    /// and time-range filtering. Returns BM25-ranked results.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery(Name = "q")] string query,
        [FromQuery(Name = "channel")] string? channel = null,
        [FromQuery(Name = "level")] byte? level = null,
        [FromQuery(Name = "computer")] string? computer = null,
        [FromQuery(Name = "time_start")] DateTimeOffset? timeStart = null,
        [FromQuery(Name = "time_end")] DateTimeOffset? timeEnd = null,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "size")] int size = 50,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = new SearchQuery(
            Query: query ?? "",
            PageSize: Math.Clamp(size, 1, 200),
            Skip: (page - 1) * size,
            Channel: channel,
            Level: level,
            Computer: computer,
            TimeStart: timeStart,
            TimeEnd: timeEnd);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _search.SearchAsync(searchQuery, cancellationToken);
        stopwatch.Stop();
        result.TookMs = stopwatch.ElapsedMilliseconds;

        _logger.LogInformation(
            "Audit: FTS5 search '{Query}' returned {Count} of {Total} results ({TookMs}ms) by {Username}.",
            query, result.Events.Count, result.TotalCount, result.TookMs, User.Identity?.Name);

        return Ok(new
        {
            query,
            total = result.TotalCount,
            page,
            size,
            took_ms = result.TookMs,
            results = result.Events.Select(e => new
            {
                e.RecordId,
                e.Channel,
                e.EventId,
                e.Provider,
                e.Level,
                e.LevelText,
                e.TimeCreatedUtc,
                e.Computer,
                e.UserSid,
                e.IngestedAtUtc
            })
        });
    }
}
