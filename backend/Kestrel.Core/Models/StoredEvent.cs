namespace Kestrel.Core.Models;

/// <summary>
/// One normalized event row destined for the <c>events</c> table. The raw
/// event XML is stored verbatim; formatted messages are produced lazily at
/// query time (Phase 3+) — never in the ingest hot path (.clinerules).
/// Forward-compatible columns (FP tuning fields, correlation/thread/process,
/// keywords, level_text) exist from day one per AGENTS.md.
/// </summary>
public sealed record StoredEvent(
    long? RecordId,
    string Channel,
    long EventId,
    string Provider,
    byte Level,
    string LevelText,
    DateTimeOffset TimeCreatedUtc,
    string Computer,
    string? UserSid,
    long? Keywords,
    string? CorrelationId,
    int? ThreadId,
    int? ProcessId,
    string Xml,
    DateTimeOffset IngestedAtUtc);

/// <summary>Standard Windows event level values (kept for query surface + Sigma pre-filter).</summary>
public static class EventLevels
{
    public static string ToText(byte level) => level switch
    {
        0 => "LogAlways",
        1 => "Critical",
        2 => "Error",
        3 => "Warning",
        4 => "Information",
        5 => "Verbose",
        _ => $"Level{level}",
    };
}