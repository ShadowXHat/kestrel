using Kestrel.Core.Models;

namespace Kestrel.Core.Ingest;

/// <summary>
/// Streams one EVTX job into the event store. Fatal problems throw (the job
/// processor records the reason); per-record problems are counted and retained
/// on the job — no silent failures (.clinerules).
/// </summary>
public interface IEvtxIngestService
{
    Task IngestAsync(IngestJob job, CancellationToken cancellationToken);
}