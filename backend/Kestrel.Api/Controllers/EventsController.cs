using Kestrel.Api.Auth;
using Kestrel.Core.Search;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

/// <summary>
/// Phase 4: Event query via /api/search.
/// Delegates to SearchService for FTS5 full-text search.
/// </summary>
[ApiController]
[Route("api/events")]
[Authorize(Policy = Policies.Viewer)]
[Produces("application/json")]
public class EventsController : ControllerBase
{
    private readonly SearchService _search;
    private readonly ILogger<EventsController> _logger;

    public EventsController(SearchService search, ILogger<EventsController> logger)
    {
        _search = search;
        _logger = logger;
    }

    /// <summary>
    /// Queries events using FTS5 full-text search with pagination,
    /// channel/level filters, and time-range filtering.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> QueryEvents(
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
        var searchQuery = new Kestrel.Core.Search.SearchQuery(
            Query: query ?? "",
            PageSize: Math.Clamp(size, 1, 200),
            Skip: (page - 1) * size,
            Channel: channel,
            Level: level,
            Computer: computer,
            TimeStart: timeStart,
            TimeEnd: timeEnd);

        var result = await _search.SearchAsync(searchQuery, cancellationToken);

        return Ok(new
        {
            query,
            total = result.TotalCount,
            page,
            size,
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
