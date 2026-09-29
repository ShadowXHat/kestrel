using Kestrel.Api.Controllers;
using Kestrel.Core.Jobs;
using Kestrel.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Kestrel.Api.Hubs;

/// <summary>Well-known SignalR groups (clients join via EventsHub.Subscribe).</summary>
public static class ProgressGroups
{
    public const string IngestJobs = "ingest-jobs";
}

/// <summary>
/// Pushes ingest job progress to <see cref="EventsHub"/> group
/// <see cref="ProgressGroups.IngestJobs"/>. Best-effort by design: a push
/// failure is logged and never kills the ingest job — but never silent.
/// Messages carry job status only, never event data.
/// </summary>
public sealed class EventsHubIngestProgressSink : IIngestProgressSink
{
    private readonly IHubContext<EventsHub> _hubContext;
    private readonly ILogger<EventsHubIngestProgressSink> _logger;

    public EventsHubIngestProgressSink(IHubContext<EventsHub> hubContext, ILogger<EventsHubIngestProgressSink> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async ValueTask OnProgressAsync(IngestJob job)
    {
        try
        {
            await _hubContext.Clients.Group(ProgressGroups.IngestJobs).SendAsync("IngestProgress", job.ToDto());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ingest progress push failed for job {JobId}.", job.Id);
        }
    }
}