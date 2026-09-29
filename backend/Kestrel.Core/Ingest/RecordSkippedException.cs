namespace Kestrel.Core.Ingest;

/// <summary>
/// A record that cannot be normalized is skipped, counted and retained on the
/// job — never silently dropped (.clinerules: per-record error handling).
/// </summary>
public sealed class RecordSkippedException : Exception
{
    public long? RecordId { get; }

    public RecordSkippedException(long? recordId, string reason)
        : base(reason)
    {
        RecordId = recordId;
    }
}