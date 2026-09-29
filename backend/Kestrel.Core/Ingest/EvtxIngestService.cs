using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text.RegularExpressions;
using Kestrel.Core.Jobs;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kestrel.Core.Ingest;

/// <summary>
/// EVTX ingest via System.Diagnostics.Eventing.Reader (the native Windows
/// API — free message formatting, Microsoft-maintained; no third-party
/// parser). Streams records out of the file, normalizes them and writes them
/// to the event store in transactional batches.
///
/// Rules honored here (docs/PHASE-2.md):
///   - No message rendering in the hot path: the raw event XML is stored
///     verbatim; formatted messages are produced lazily at query time.
///   - Per-record error handling: a bad record is counted, retained (bounded)
///     and logged — the job continues.
///   - Fatal problems (unreadable file, timeout, record cap, write failure)
///     throw; the job processor lands the job in a terminal state with a reason.
/// </summary>
public sealed class EvtxIngestService : IEvtxIngestService
{
    // Field extraction from the event's own XML. Parsed at ingest time (not
    // query time) with compiled regexes — cheap, single-pass, no XML DOM per
    // record. Everything <EventID>/<Channel>/... is inside the <System> node.
    private static readonly Regex ChannelRx =
        new(@"<Channel>([^<]{1,512})</Channel>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EventIdRx =
        new(@"<EventID[^>]*>\s*(\d{1,10})\s*</EventID>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProviderRx =
        new(@"<Provider\s[^>]*Name=""([^""]{1,256})""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ComputerRx =
        new(@"<Computer>([^<]{1,256})</Computer>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UserSidRx =
        new(@"<Security\s[^>]*UserID=""([^""]{1,128})""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SystemTimeRx =
        new(@"<TimeCreated\s[^>]*SystemTime=""([^""]{1,64})""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex KeywordsRx =
        new(@"<Keywords>(\d{1,20})</Keywords>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ProcessIdRx =
        new(@"<Execution\s[^>]*ProcessID=""(\d{1,10})""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ThreadIdRx =
        new(@"<Execution\s[^>]*ThreadID=""(\d{1,10})""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CorrelationIdRx =
        new(@"<Correlation\s[^>]*ActivityID=""\{([0-9a-fA-F-]{36})\}""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly EventStore _eventStore;
    private readonly IngestJobStore _jobStore;
    private readonly IngestOptions _options;
    private readonly IIngestProgressSink _progressSink;
    private readonly ILogger<EvtxIngestService> _logger;

    public EvtxIngestService(
        EventStore eventStore,
        IngestJobStore jobStore,
        IngestOptions options,
        IIngestProgressSink progressSink,
        ILogger<EvtxIngestService> logger)
    {
        _eventStore = eventStore;
        _jobStore = jobStore;
        _options = options;
        _progressSink = progressSink;
        _logger = logger;
    }

    public async Task IngestAsync(IngestJob job, CancellationToken cancellationToken)
    {
        var batch = new List<StoredEvent>(_options.BatchSize);
        var query = new EventLogQuery(job.FilePath, PathType.FilePath);
        using var reader = new EventLogReader(query);

        var attemptedFirstRead = false;
        var consecutiveReadFailures = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            EventRecord? record;
            try
            {
                record = reader.ReadEvent();
            }
            catch (EventLogException ex)
            {
                // A read failure before ANY record arrived is file-level
                // (truncated header, structural damage, reader setup) — fatal
                // for the whole job.
                if (!attemptedFirstRead)
                {
                    throw;
                }

                // Mid-stream: one unreadable record, not a broken file — skip
                // it, count it, keep going (per-record, never silent —
                // .clinerules). A bounded number of CONSECUTIVE failures means
                // the reader has stopped making progress entirely; fail the
                // job instead of spinning to the record cap.
                consecutiveReadFailures++;
                job.FailedRecords++;
                AddFirstError(job, null, $"the event could not be read from the file: {ex.Message}");
                if (consecutiveReadFailures > MaxConsecutiveReadFailures)
                {
                    throw new InvalidOperationException(
                        $"The event log reader stopped making progress after {MaxConsecutiveReadFailures} consecutive " +
                        $"unreadable records in '{job.FileName}' (last error: {ex.Message}).");
                }

                continue;
            }

            attemptedFirstRead = true;
            consecutiveReadFailures = 0;

            if (record is null)
            {
                break;
            }

            using (record)
            {
                if (job.TotalRecords >= _options.MaxRecordsPerJob)
                {
                    throw new InvalidOperationException(
                        $"Record cap exceeded: stopping ingest of '{job.FileName}' at {job.TotalRecords} records " +
                        $"(KestrelApp:Ingest:MaxRecordsPerJob={_options.MaxRecordsPerJob}).");
                }

                job.TotalRecords++;

                try
                {
                    batch.Add(MapRecord(record, FallbackChannel(job.FileName), DateTimeOffset.UtcNow));
                    job.ProcessedRecords++;
                }
                catch (RecordSkippedException ex)
                {
                    // The skip reason is carried in the exception message
                    // (RecordSkippedException forwards it to base(reason)).
                    job.FailedRecords++;
                    AddFirstError(job, ex.RecordId, ex.Message);
                }
                catch (Exception ex)
                {
                    job.FailedRecords++;
                    AddFirstError(job, record.Id, $"unexpected failure while mapping record: {ex.Message}");
                }

                if (batch.Count >= _options.BatchSize)
                {
                    await FlushAsync(job, batch, cancellationToken);
                }
            }
        }

        if (batch.Count > 0)
        {
            await FlushAsync(job, batch, cancellationToken);
        }

        _logger.LogInformation(
            "Ingest {JobId}: finished reading '{FileName}' — {Total} records, {Processed} ingested, {Failed} skipped.",
            job.Id, job.FileName, job.TotalRecords, job.ProcessedRecords, job.FailedRecords);
    }

    /// <summary>
    /// Consecutive unreadable records tolerated before the job is failed as
    /// unreadable: beyond this bound the reader is not making progress (a
    /// fundamentally broken file), so continuing would just spin.
    /// </summary>
    private const int MaxConsecutiveReadFailures = 100;

    private async Task FlushAsync(IngestJob job, List<StoredEvent> batch, CancellationToken cancellationToken)
    {
        await _eventStore.InsertBatchAsync(job.Id, batch, cancellationToken);
        batch.Clear();

        // Persist progress + push it (SignalR) once per batch — bounded work,
        // and the job stays queryable while a large file is still ingesting.
        await _jobStore.SetCountersAsync(job, cancellationToken);
        await _progressSink.OnProgressAsync(job);
    }

    private void AddFirstError(IngestJob job, long? recordId, string reason)
    {
        var trimmed = reason.Length > 500 ? reason[..500] : reason;

        if (job.FirstErrors.Count < _options.MaxPerRecordErrorsRetained)
        {
            job.FirstErrors = job.FirstErrors.Append(new IngestRecordError(recordId, trimmed)).ToArray();
            _logger.LogWarning(
                "Ingest {JobId}: record {RecordId} failed and was skipped: {Reason}",
                job.Id, recordId, trimmed);
        }
        else
        {
            // Beyond the retained-error budget: still counted and logged —
            // never silent — just not stored on the job (bounded memory).
            _logger.LogWarning(
                "Ingest {JobId}: record {RecordId} failed beyond the retained-error budget: {Reason}",
                job.Id, recordId, trimmed);
        }
    }

    private StoredEvent MapRecord(EventRecord record, string fallbackChannel, DateTimeOffset ingestedAtUtc)
    {
        var xml = record.ToXml();
        if (string.IsNullOrEmpty(xml))
        {
            throw new RecordSkippedException(record.Id, "the event has no XML representation");
        }

        var eventIdMatch = EventIdRx.Match(xml);
        if (!eventIdMatch.Success)
        {
            throw new RecordSkippedException(record.Id, "no <EventID> element in the event XML");
        }

        var eventId = long.Parse(eventIdMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        var timeCreatedUtc = ResolveTimeCreated(record, xml);
        // EventRecord.Level is byte? (providers may omit it); the store keeps a
        // non-nullable level, so a missing level maps to 0 (LogAlways).
        var level = record.Level ?? 0;
        var channel = MatchOrNull(ChannelRx, xml) ?? fallbackChannel;
        var provider = MatchOrNull(ProviderRx, xml) ?? record.ProviderName ?? string.Empty;
        var computer = MatchOrNull(ComputerRx, xml) ?? record.MachineName ?? string.Empty;
        var userSid = record.UserId?.Value ?? MatchOrNull(UserSidRx, xml);
        var keywords = record.Keywords ?? ParseNullableLong(KeywordsRx, xml);

        return new StoredEvent(
            RecordId: record.Id,
            Channel: channel,
            EventId: eventId,
            Provider: provider,
            Level: level,
            LevelText: EventLevels.ToText(level),
            TimeCreatedUtc: timeCreatedUtc,
            Computer: computer,
            UserSid: userSid,
            Keywords: keywords,
            CorrelationId: MatchOrNull(CorrelationIdRx, xml),
            ThreadId: ParseNullableInt(ThreadIdRx, xml),
            ProcessId: ParseNullableInt(ProcessIdRx, xml),
            Xml: xml,
            IngestedAtUtc: ingestedAtUtc);
    }

    private static DateTimeOffset ResolveTimeCreated(EventRecord record, string xml)
    {
        if (record.TimeCreated is { } timeCreated)
        {
            // The Windows API reports wall-clock local time; normalize to UTC
            // (Unspecified is treated as local per the Win32 eventing contract).
            var utc = timeCreated.Kind == DateTimeKind.Utc
                ? timeCreated
                : DateTime.SpecifyKind(timeCreated, DateTimeKind.Local).ToUniversalTime();
            return new DateTimeOffset(utc, TimeSpan.Zero);
        }

        var match = SystemTimeRx.Match(xml);
        if (match.Success &&
            DateTimeOffset.TryParse(
                match.Groups[1].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw new RecordSkippedException(record.Id, "no usable timestamp on the event");
    }

    private static string? MatchOrNull(Regex regex, string xml)
    {
        var match = regex.Match(xml);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static long? ParseNullableLong(Regex regex, string xml)
    {
        var match = regex.Match(xml);
        return match.Success &&
               long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? ParseNullableInt(Regex regex, string xml)
    {
        var match = regex.Match(xml);
        return match.Success &&
               int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string FallbackChannel(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }
}