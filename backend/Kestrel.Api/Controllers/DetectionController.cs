using Kestrel.Api.Auth;
using Kestrel.Core.Detection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kestrel.Api.Controllers;

/// <summary>
/// Sigma detection endpoints (Phase 5): list the compiled rule pack, compile /
/// validate a rule (or reload the pack), and run the engine over stored events.
/// Listing rules is read-only reporting (Viewer); compiling arbitrary YAML and
/// running scans are Analyst operations — compute-heavy and rule-authoring
/// surfaces stay out of the Viewer role.
/// </summary>
[ApiController]
[Route("api/detection")]
[Authorize]
[Produces("application/json")]
public class DetectionController : ControllerBase
{
    private readonly DetectionEngine _engine;
    private readonly RulePack _rulePack;
    private readonly AttackTechniqueStore _attack;
    private readonly ILogger<DetectionController> _logger;

    public DetectionController(
        DetectionEngine engine,
        RulePack rulePack,
        AttackTechniqueStore attack,
        ILogger<DetectionController> logger)
    {
        _engine = engine;
        _rulePack = rulePack;
        _attack = attack;
        _logger = logger;
    }

    /// <summary>
    /// Lists the compiled Sigma rule pack with per-rule metadata, ATT&amp;CK
    /// technique mapping and any validation warnings, plus the files that
    /// failed to load (with reasons — never silently skipped).
    /// </summary>
    [HttpGet("rules")]
    [Authorize(Policy = Policies.Viewer)]
    public IActionResult Rules()
    {
        var pack = _rulePack.Current;

        var rules = pack.Rules.Select(rule => new
        {
            id = rule.Rule.Id,
            title = rule.Rule.Title,
            status = rule.Rule.Status.ToString().ToLowerInvariant(),
            level = rule.Rule.Level.ToString().ToLowerInvariant(),
            description = rule.Rule.Description,
            author = rule.Rule.Author,
            date = rule.Rule.Date,
            logsource = rule.Rule.LogSource is null
                ? null
                : new
                {
                    category = rule.Rule.LogSource.Category,
                    product = rule.Rule.LogSource.Product,
                    service = rule.Rule.LogSource.Service,
                },
            tags = rule.Rule.Tags,
            condition = RenderCondition(rule.Rule.Detection.Condition),
            selector_count = rule.Rule.Detection.Selectors.Count,
            file = rule.SourceFile,
            technique_ids = rule.TechniqueIds,
            tactics = rule.TacticTags,
            techniques = rule.TechniqueIds.Select(TechniqueInfo).ToList(),
            warnings = rule.Warnings
                .Select(w => new { severity = "warning", code = w.Code, message = w.Message })
                .ToList(),
        }).ToList();

        _logger.LogInformation(
            "Audit: rule pack listing returned {Rules} rule(s), {Failures} failed file(s) by {Username}.",
            rules.Count, pack.Failures.Count, User.Identity?.Name);

        return Ok(new
        {
            source_directory = pack.SourceDirectory,
            total = rules.Count,
            failed_files = pack.Failures.Count,
            rules,
            failures = pack.Failures.Select(f => new { file = f.File, error = f.Error }).ToList(),
        });
    }

    /// <summary>
    /// Compiles a Sigma rule supplied as YAML (validating it before
    /// compilation), or re-loads the on-disk rule pack when
    /// <c>reload: true</c>. Compilation failures return 400 with the full
    /// validation diagnostics.
    /// </summary>
    [HttpPost("compile")]
    [Authorize(Policy = Policies.Analyst)]
    public IActionResult Compile([FromBody] CompileRequest request)
    {
        if (request.Reload)
        {
            var pack = _rulePack.Reload();
            _logger.LogInformation(
                "Audit: rule pack reload by {Username}: {Rules} rule(s), {Failures} failed file(s).",
                User.Identity?.Name, pack.Rules.Count, pack.Failures.Count);

            return Ok(new
            {
                reloaded = true,
                total = pack.Rules.Count,
                failed_files = pack.Failures.Count,
                rules = pack.Rules.Select(r => new { id = r.Rule.Id, title = r.Rule.Title, file = r.SourceFile }),
                failures = pack.Failures.Select(f => new { file = f.File, error = f.Error }).ToList(),
            });
        }

        if (string.IsNullOrWhiteSpace(request.Yaml))
        {
            return BadRequest(new
            {
                error = "invalid_request",
                message = "provide 'yaml' or set 'reload' to true.",
            });
        }

        var compiler = new SigmaCompiler();
        try
        {
            var compiled = compiler.CompileYaml(request.Yaml, sourceFile: request.Name ?? "<inline>");
            _logger.LogInformation(
                "Audit: inline Sigma rule '{Title}' compiled by {Username} ({Warnings} warning(s)).",
                compiled.Rule.Title, User.Identity?.Name, compiled.Warnings.Count);

            return Ok(new
            {
                compiled = true,
                warnings = compiled.Warnings
                    .Select(w => new { severity = "warning", code = w.Code, message = w.Message })
                    .ToList(),
                technique_ids = compiled.TechniqueIds,
                tactics = compiled.TacticTags,
                prefilter = new
                {
                    channels = compiled.Prefilter.Channels,
                    event_ids = compiled.Prefilter.EventIds,
                },
                sql_where = compiled.WhereClause,
                rule = new
                {
                    id = compiled.Rule.Id,
                    title = compiled.Rule.Title,
                    status = compiled.Rule.Status.ToString().ToLowerInvariant(),
                    level = compiled.Rule.Level.ToString().ToLowerInvariant(),
                    tags = compiled.Rule.Tags,
                },
            });
        }
        catch (SigmaParseException ex)
        {
            return BadRequest(new
            {
                compiled = false,
                error = "parse_error",
                message = ex.Message,
                issues = Array.Empty<object>(),
            });
        }
        catch (SigmaCompileException ex)
        {
            return BadRequest(new
            {
                compiled = false,
                error = "validation_error",
                message = ex.Message,
                issues = ex.Errors.Select(e => new
                {
                    severity = e.Severity.ToString().ToLowerInvariant(),
                    code = e.Code,
                    message = e.Message,
                }).ToList(),
            });
        }
    }

    /// <summary>
    /// Runs the detection engine over the stored events table and returns
    /// matching events as alerts with their ATT&amp;CK technique mapping.
    /// Results are bounded per rule (see Sigma:MaxMatchesPerRule).
    /// </summary>
    [HttpPost("run")]
    [Authorize(Policy = Policies.Analyst)]
    public async Task<IActionResult> Run(
        [FromBody] RunRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new RunRequest();
        var query = new DetectionScanQuery(
            RuleId: request.RuleId,
            JobId: request.JobId,
            Channel: request.Channel,
            EventId: request.EventId,
            MaxMatchesPerRule: Math.Clamp(request.Limit ?? 1_000, 1, 100_000));

        var result = await _engine.ScanAsync(query, cancellationToken);

        _logger.LogInformation(
            "Audit: detection run by {Username}: rule filter {RuleId}, {Rules} rule(s), {Alerts} alert(s), {TookMs}ms.",
            User.Identity?.Name, request.RuleId ?? "(all)", result.RulesEvaluated, result.Alerts.Count,
            result.ElapsedMs);

        return Ok(new
        {
            rules_evaluated = result.RulesEvaluated,
            took_ms = result.ElapsedMs,
            truncated_rules = result.TruncatedRules,
            alerts = result.Alerts.Select(a => new
            {
                rule_id = a.RuleId,
                rule_title = a.RuleTitle,
                rule_level = a.RuleLevel.ToString().ToLowerInvariant(),
                source_file = a.SourceFile,
                event_row_id = a.EventRowId,
                record_id = a.RecordId,
                channel = a.Channel,
                event_id = a.EventId,
                computer = a.Computer,
                time_created_utc = a.TimeCreatedUtc,
                technique_ids = a.TechniqueIds,
                tactics = a.Tactics,
                techniques = a.Techniques.Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    tactics = t.Tactics,
                    url = t.Url,
                }),
                matched_selectors = a.MatchedSelectors,
            }).ToList(),
        });
    }

    private object TechniqueInfo(string techniqueId)
    {
        var technique = _attack.Find(techniqueId);
        return technique is null
            ? new { id = techniqueId, name = (string?)null, tactics = Array.Empty<string>(), url = (string?)null }
            : new { id = technique.Id, name = technique.Name, tactics = technique.Tactics, url = technique.Url };
    }

    private static string RenderCondition(Kestrel.Core.Detection.SigmaConditionNode node) => node switch
    {
        SigmaSelectorCondition selector => selector.Name,
        SigmaAndCondition and => "(" + string.Join(" AND ", and.Terms.Select(RenderCondition)) + ")",
        SigmaOrCondition orCondition => "(" + string.Join(" OR ", orCondition.Terms.Select(RenderCondition)) + ")",
        SigmaNotCondition notCondition => $"NOT {RenderCondition(notCondition.Inner)}",
        SigmaOfCondition ofCondition when ofCondition.IsAll =>
            $"all of ({string.Join(", ", ofCondition.Terms.OfType<SigmaSelectorCondition>().Select(t => t.Name))})",
        SigmaOfCondition ofCondition =>
            $"{ofCondition.Count} of ({string.Join(", ", ofCondition.Terms.OfType<SigmaSelectorCondition>().Select(t => t.Name))})",
        _ => "(complex)",
    };

    public sealed record CompileRequest(string? Yaml = null, string? Name = null, bool Reload = false);

    public sealed record RunRequest(
        string? RuleId = null,
        string? JobId = null,
        string? Channel = null,
        long? EventId = null,
        int? Limit = null);
}


