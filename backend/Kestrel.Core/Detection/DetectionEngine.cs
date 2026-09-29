using System.Diagnostics;
using System.Globalization;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using Microsoft.Extensions.Logging;

namespace Kestrel.Core.Detection;

/// <summary>Scope for a stored-events scan.</summary>
public sealed record DetectionScanQuery(
    string? RuleId = null,
    string? JobId = null,
    string? Channel = null,
    long? EventId = null,
    int MaxMatchesPerRule = 1_000)
{
    public static DetectionScanQuery Default { get; } = new();
}

/// <summary>One detection alert: the matched event plus the rule and its ATT&amp;CK mapping.</summary>
public sealed record DetectionAlert(
    string RuleId,
    string RuleTitle,
    SigmaSeverity RuleLevel,
    string SourceFile,
    long EventRowId,
    long? RecordId,
    string Channel,
    long EventId,
    string Computer,
    DateTimeOffset TimeCreatedUtc,
    IReadOnlyList<string> TechniqueIds,
    IReadOnlyList<AttackTechnique> Techniques,
    IReadOnlyList<string> Tactics,
    IReadOnlySet<string> MatchedSelectors);

/// <summary>Result of an in-memory batch match.</summary>
public sealed record MatchBatchResult(
    IReadOnlyList<DetectionAlert> Alerts,
    int EventsScanned,
    int RulesEvaluated,
    long ElapsedMs);

/// <summary>Result of a stored-events scan.</summary>
public sealed record DetectionScanResult(
    IReadOnlyList<DetectionAlert> Alerts,
    int RulesEvaluated,
    IReadOnlyList<string> TruncatedRules,
    long ElapsedMs);

/// <summary>
/// Runs compiled Sigma rules and produces alerts with ATT&amp;CK TTP mapping.
/// Two execution paths:
///   - <see cref="MatchBatch"/>: in-memory evaluation of event batches
///     (channel+EventID pre-filter → FastMatch → condition evaluation).
///   - <see cref="ScanAsync"/>: set-based SQL scans over the stored events
///     table using each rule's compiled parameterized WHERE clause.
/// </summary>
public sealed class DetectionEngine
{
    private const string ScanColumns =
        "e.id, e.record_id, e.channel, e.event_id, e.level, e.level_text, e.time_created_utc, e.computer";

    private readonly EventDatabase _database;
    private readonly RulePack _rulePack;
    private readonly AttackTechniqueStore _attack;
    private readonly SigmaOptions _options;
    private readonly ILogger<DetectionEngine> _logger;

    public DetectionEngine(
        EventDatabase database,
        RulePack rulePack,
        AttackTechniqueStore attack,
        SigmaOptions options,
        ILogger<DetectionEngine> logger)
    {
        _database = database;
        _rulePack = rulePack;
        _attack = attack;
        _options = options;
        _logger = logger;
    }

    /// <summary>The rule pack this engine evaluates.</summary>
    public CompiledRulePack Pack => _rulePack.Current;

    /// <summary>
    /// Runs every rule (or one rule) against an in-memory event batch —
    /// the entry point for live/event-driven matching. Per-rule failures are
    /// logged and skipped, never silent.
    /// </summary>
    public MatchBatchResult MatchBatch(
        IReadOnlyList<StoredEvent> events,
        string? ruleId = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var pack = _rulePack.Current;
        var rules = ruleId is null ? pack.Rules : MaybeSingle(pack.Find(ruleId), ruleId);
        var alerts = new List<DetectionAlert>();

        foreach (var rule in rules)
        {
            try
            {
                var techniqueIds = rule.TechniqueIds;
                var tactics = rule.TacticTags;
                foreach (var storedEvent in events)
                {
                    // Sound pre-filter: skip events the rule can never match.
                    if (!rule.CouldMatch(storedEvent))
                    {
                        continue;
                    }

                    if (rule.Evaluate(storedEvent, out var matchedSelectors))
                    {
                        alerts.Add(BuildAlert(rule, storedEvent, eventRowId: storedEvent.RecordId ?? -1,
                            techniqueIds, tactics, matchedSelectors));
                    }
                }
            }
            catch (Exception ex)
            {
                // Per-rule error isolation — a bad rule never stops the batch.
                _logger.LogError(ex,
                    "Detection rule '{RuleId}' ('{Title}') failed during batch matching: {Reason}",
                    rule.Rule.Id, rule.Rule.Title, ex.Message);
            }
        }

        stopwatch.Stop();
        return new MatchBatchResult(alerts, events.Count, rules.Count, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Set-based scan: runs each rule's compiled SQL against the stored events
    /// table and returns alerts. Output is bounded per rule by
    /// <paramref name="query.MaxMatchesPerRule"/> (clamped to the configured
    /// hard cap) — truncated rules are listed in the result.
    /// </summary>
    public async Task<DetectionScanResult> ScanAsync(
        DetectionScanQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= DetectionScanQuery.Default;
        var maxPerRule = Math.Clamp(query.MaxMatchesPerRule, 1, _options.MaxMatchesPerRule);

        var stopwatch = Stopwatch.StartNew();
        var pack = _rulePack.Current;
        var rules = query.RuleId is null
            ? pack.Rules
            : MaybeSingle(pack.Find(query.RuleId), query.RuleId);

        var alerts = new List<DetectionAlert>();
        var truncated = new List<string>();

        await using var connection = await _database.OpenConnectionAsync(cancellationToken);

        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (await ScanRuleAsync(connection, rule, query, maxPerRule, alerts, cancellationToken))
                {
                    truncated.Add(rule.Rule.Id is { Length: > 0 } id ? id : rule.Rule.Title);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-rule, per-scan error isolation with the reason attached.
                _logger.LogError(ex,
                    "Detection rule '{RuleId}' ('{Title}') failed to scan: {Reason}",
                    rule.Rule.Id, rule.Rule.Title, ex.Message);
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Detection scan: {Rules} rule(s), {Alerts} alert(s), {Truncated} truncated rule(s), {ElapsedMs}ms.",
            rules.Count, alerts.Count, truncated.Count, stopwatch.ElapsedMilliseconds);

        return new DetectionScanResult(alerts, rules.Count, truncated, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>Scans one rule; returns true when the per-rule match budget truncated it.</summary>
    private async Task<bool> ScanRuleAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CompiledSigmaRule rule,
        DetectionScanQuery query,
        int maxPerRule,
        List<DetectionAlert> alerts,
        CancellationToken cancellationToken)
    {
        // Cheap scope constraints first (index-friendly), then the rule's WHERE.
        var conditions = new List<string>();
        var logSourceChannels = rule.LogSourceChannels?.OrderBy(c => c, StringComparer.Ordinal).ToList();
        var channelParameterNames = new List<string>();

        if (query.JobId is not null)
        {
            conditions.Add("e.job_id = $job_id");
        }

        if (query.Channel is not null)
        {
            conditions.Add("e.channel = $channel");
        }

        if (query.EventId is not null)
        {
            conditions.Add("e.event_id = $event_id");
        }

        if (logSourceChannels is not null)
        {
            for (var index = 0; index < logSourceChannels.Count; index++)
            {
                channelParameterNames.Add($"$ls_channel_{index}");
            }

            conditions.Add($"e.channel COLLATE NOCASE IN ({string.Join(", ", channelParameterNames)})");
        }

        conditions.Add(rule.WhereClause);
        var where = string.Join(" AND ", conditions.Select(c => $"({c})"));

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT {ScanColumns}
             FROM events e
             WHERE {where}
             ORDER BY e.id
             LIMIT $limit_plus_one;
             """;
        command.Parameters.AddWithValue("$limit_plus_one", maxPerRule + 1);

        if (query.JobId is not null)
        {
            command.Parameters.AddWithValue("$job_id", query.JobId);
        }

        if (query.Channel is not null)
        {
            command.Parameters.AddWithValue("$channel", query.Channel);
        }

        if (query.EventId is not null)
        {
            command.Parameters.AddWithValue("$event_id", query.EventId);
        }

        if (logSourceChannels is not null)
        {
            for (var index = 0; index < logSourceChannels.Count; index++)
            {
                command.Parameters.AddWithValue(channelParameterNames[index], logSourceChannels[index]);
            }
        }

        foreach (var (name, value) in rule.Parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var techniqueIds = rule.TechniqueIds;
        var tactics = rule.TacticTags;
        var truncated = false;
        var read = 0;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (read >= maxPerRule)
            {
                truncated = true; // limit+1 row present — the budget cut matches off
                break;
            }

            var eventRowId = reader.GetInt64(0);
            var timeCreated = DateTimeOffset.Parse(
                reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            var storedEvent = new StoredEvent(
                RecordId: reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Channel: reader.GetString(2),
                EventId: reader.GetInt64(3),
                Provider: string.Empty,
                Level: Convert.ToByte(reader.GetInt64(4), CultureInfo.InvariantCulture),
                LevelText: reader.GetString(5),
                TimeCreatedUtc: timeCreated,
                Computer: reader.GetString(7),
                UserSid: null,
                Keywords: null,
                CorrelationId: null,
                ThreadId: null,
                ProcessId: null,
                Xml: string.Empty,
                IngestedAtUtc: timeCreated);

            // The SQL WHERE already proved the match; per-alert selector
            // diagnostics stay empty in the SQL path (the WHERE clause is a
            // single expression, not a selector trace).
            alerts.Add(BuildAlert(rule, storedEvent, eventRowId, techniqueIds, tactics,
                new HashSet<string>(StringComparer.Ordinal)));
            read++;
        }

        return truncated;
    }

    private static IReadOnlyList<CompiledSigmaRule> MaybeSingle(CompiledSigmaRule? rule, string ruleId)
    {
        return rule is null
            ? []
            : [rule];
    }

    private DetectionAlert BuildAlert(
        CompiledSigmaRule rule,
        StoredEvent storedEvent,
        long eventRowId,
        IReadOnlyList<string> techniqueIds,
        IReadOnlyList<string> tactics,
        HashSet<string> matchedSelectors)
    {
        var techniques = new List<AttackTechnique>();
        foreach (var techniqueId in techniqueIds)
        {
            var technique = _attack.Find(techniqueId);
            if (technique is not null)
            {
                techniques.Add(technique);
            }
        }

        return new DetectionAlert(
            RuleId: rule.Rule.Id ?? rule.Rule.Title,
            RuleTitle: rule.Rule.Title,
            RuleLevel: rule.Rule.Level,
            SourceFile: rule.SourceFile,
            EventRowId: eventRowId,
            RecordId: storedEvent.RecordId,
            Channel: storedEvent.Channel,
            EventId: storedEvent.EventId,
            Computer: storedEvent.Computer,
            TimeCreatedUtc: storedEvent.TimeCreatedUtc,
            TechniqueIds: techniqueIds,
            Techniques: techniques,
            Tactics: tactics,
            MatchedSelectors: matchedSelectors);
    }
}



