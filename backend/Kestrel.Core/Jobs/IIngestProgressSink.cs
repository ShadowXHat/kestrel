using Kestrel.Core.Models;

namespace Kestrel.Core.Jobs;

/// <summary>
/// Progress / terminal-state notifications for ingest jobs (e.g. a SignalR
/// push in the Api layer). Implementations must be resilient: a sink failure
/// is logged by the processor and never kills the ingest job.
/// </summary>
public interface IIngestProgressSink
{
    ValueTask OnProgressAsync(IngestJob job);
}

/// <summary>No-op sink for tests and non-pushing hosts.</summary>
public sealed class NullIngestProgressSink : IIngestProgressSink
{
    public static readonly NullIngestProgressSink Instance = new();

    public ValueTask OnProgressAsync(IngestJob job) => ValueTask.CompletedTask;
}