using System.Threading.Channels;
using Kestrel.Core.Models;

namespace Kestrel.Core.Jobs;

/// <summary>
/// Bounded FIFO of pending ingest jobs (memory bound — .clinerules). TryEnqueue
/// reports failure when the queue is full, so callers can answer 503 instead
/// of buffering silently.
/// </summary>
public sealed class IngestJobQueue
{
    public const int Capacity = 16;

    private readonly Channel<IngestJob> _channel = Channel.CreateBounded<IngestJob>(new BoundedChannelOptions(Capacity)
    {
        SingleReader = true,
        SingleWriter = false,
    });

    /// <summary>Enqueues without waiting; false means the queue is full.</summary>
    public bool TryEnqueue(IngestJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<IngestJob> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}