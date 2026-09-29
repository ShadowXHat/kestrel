using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kestrel.Core.Detection;

/// <summary>Sigma rule-pack configuration (<c>Sigma</c> section).</summary>
public sealed record SigmaOptions
{
    public const string SectionName = "Sigma";

    public const string DefaultRulePackPath = "rules";
    public const int DefaultMaxMatchesPerRule = 10_000;

    /// <summary>Directory the rule pack is loaded from (content-root relative by default).</summary>
    public string RulePackPath { get; init; } = DefaultRulePackPath;

    /// <summary>Hard cap on matches kept per rule per scan (memory bounds).</summary>
    public int MaxMatchesPerRule { get; init; } = DefaultMaxMatchesPerRule;

    public static SigmaOptions FromConfiguration(IConfiguration configuration, string contentRootPath)
    {
        var rulePackPath = configuration[$"{SectionName}:RulePackPath"];
        if (string.IsNullOrWhiteSpace(rulePackPath))
        {
            rulePackPath = DefaultRulePackPath;
        }

        if (!Path.IsPathRooted(rulePackPath))
        {
            rulePackPath = Path.Combine(contentRootPath, rulePackPath);
        }

        var maxMatchesPerRule = DefaultMaxMatchesPerRule;
        if (int.TryParse(configuration[$"{SectionName}:MaxMatchesPerRule"], out var parsed) &&
            parsed is >= 1 and <= 1_000_000)
        {
            maxMatchesPerRule = parsed;
        }

        return new SigmaOptions { RulePackPath = rulePackPath, MaxMatchesPerRule = maxMatchesPerRule };
    }
}

/// <summary>One rule-pack file that failed to load (file + reason — no silent failures).</summary>
public sealed record RulePackFailure(string File, string Error);

/// <summary>An immutable snapshot of a loaded rule pack.</summary>
public sealed record CompiledRulePack(
    IReadOnlyList<CompiledSigmaRule> Rules,
    IReadOnlyList<RulePackFailure> Failures,
    string SourceDirectory)
{
    public CompiledSigmaRule? Find(string ruleId) =>
        Rules.FirstOrDefault(r =>
            string.Equals(r.Rule.Id, ruleId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Loads and manages the Sigma rule pack from the rules directory
/// (<c>Sigma:RulePackPath</c>). Every <c>*.yml</c>/<c>*.yaml</c> file is parsed,
/// validated and compiled independently: one broken file becomes a recorded
/// failure and never prevents the rest of the pack from loading. Files starting
/// with an underscore (partials) are skipped. Duplicate rule IDs (Sigma requires
/// unique UUIDs) are rejected as load failures.
/// </summary>
public sealed class RulePack
{
    private readonly SigmaCompiler _compiler;
    private readonly ILogger<RulePack>? _logger;
    private readonly SigmaOptions _options;
    private volatile CompiledRulePack _current;

    public RulePack(SigmaOptions options, ILogger<RulePack>? logger = null)
    {
        _options = options;
        _compiler = new SigmaCompiler();
        _logger = logger;
        _current = Load(options.RulePackPath);
    }

    /// <summary>The latest successfully loaded pack snapshot (thread-safe).</summary>
    public CompiledRulePack Current => _current;

    public string SourceDirectory => _options.RulePackPath;

    /// <summary>Re-loads the rule pack from disk (used by the compile endpoint).</summary>
    public CompiledRulePack Reload()
    {
        var pack = Load(_options.RulePackPath);
        _current = pack;
        return pack;
    }

    internal CompiledRulePack Load(string directory)
    {
        var rules = new List<CompiledSigmaRule>();
        var failures = new List<RulePackFailure>();
        var seenIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(directory))
        {
            _logger?.LogWarning(
                "Sigma rule pack directory {Directory} does not exist — the detection engine starts with an empty pack. " +
                "Create it (or set Sigma:RulePackPath) and add .yml Sigma rules.",
                directory);
            return new CompiledRulePack(rules, failures, directory);
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) ||
                                 f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            LoadRuleFile(file, rules, failures, seenIds);
        }

        _logger?.LogInformation(
            "Sigma rule pack loaded from {Directory}: {Rules} rule(s) compiled, {Failures} file(s) failed.",
            directory, rules.Count, failures.Count);

        return new CompiledRulePack(rules, failures, directory);
    }

    private void LoadRuleFile(
        string file,
        List<CompiledSigmaRule> rules,
        List<RulePackFailure> failures,
        Dictionary<string, string> seenIds)
    {
        var fileName = Path.GetFileName(file);
        if (fileName.StartsWith('_'))
        {
            return; // underscore-prefixed files are partials, not rules
        }

        try
        {
            var compiled = _compiler.CompileYaml(File.ReadAllText(file), sourceFile: fileName);
            if (compiled.Rule.Id is { Length: > 0 } id)
            {
                if (seenIds.TryGetValue(id, out var existingFile))
                {
                    failures.Add(new RulePackFailure(fileName,
                        $"duplicate rule id '{id}' (already defined in {existingFile})."));
                    return;
                }

                seenIds[id] = fileName;
            }

            rules.Add(compiled);

            foreach (var warning in compiled.Warnings)
            {
                _logger?.LogWarning(
                    "Sigma rule {File} ('{Title}') warning {Code}: {Message}",
                    fileName, compiled.Rule.Title, warning.Code, warning.Message);
            }
        }
        catch (SigmaParseException ex)
        {
            failures.Add(new RulePackFailure(fileName, ex.Message));
            _logger?.LogWarning("Sigma rule {File} failed to parse: {Reason}", fileName, ex.Message);
        }
        catch (SigmaCompileException ex)
        {
            failures.Add(new RulePackFailure(fileName, ex.Message));
            _logger?.LogWarning("Sigma rule {File} failed validation: {Reason}", fileName, ex.Message);
        }
        catch (Exception ex)
        {
            // Per-rule, per-file error handling: an unexpected I/O failure on
            // one file must not stop the pack — but it is surfaced loudly.
            failures.Add(new RulePackFailure(fileName, $"unexpected error: {ex.Message}"));
            _logger?.LogError(ex, "Unexpected error loading Sigma rule {File}.", fileName);
        }
    }
}

