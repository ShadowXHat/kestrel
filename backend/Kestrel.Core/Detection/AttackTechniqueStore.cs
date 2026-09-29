using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Kestrel.Core.Detection;

/// <summary>One MITRE ATT&amp;CK enterprise technique (from STIX attack-pattern objects).</summary>
public sealed record AttackTechnique(
    string Id,
    string Name,
    IReadOnlyList<string> Tactics,
    string? Url);

public sealed record AttackIntelOptions
{
    public const string SectionName = "Attack";

    public const string DefaultStixFilePath = "data/enterprise-attack.json";

    /// <summary>Absolute path of the STIX bundle (enterprise-attack.json).</summary>
    public string StixFilePath { get; init; } = DefaultStixFilePath;

    public static AttackIntelOptions FromConfiguration(IConfiguration configuration, string contentRootPath)
    {
        var stixFilePath = configuration[$"{SectionName}:StixFilePath"];
        if (string.IsNullOrWhiteSpace(stixFilePath))
        {
            stixFilePath = DefaultStixFilePath;
        }

        if (!Path.IsPathRooted(stixFilePath))
        {
            stixFilePath = Path.Combine(contentRootPath, stixFilePath);
        }

        return new AttackIntelOptions { StixFilePath = stixFilePath };
    }
}

/// <summary>
/// Loads MITRE ATT&amp;CK techniques from an embedded/dropped STIX bundle
/// (enterprise-attack.json, <c>Attack:StixFilePath</c>) and enriches detection
/// alerts with technique names and tactics. The store loads lazily: a missing
/// or unreadable file is logged loudly once and the store stays empty — rules
/// still carry their raw technique IDs from <c>attack.t*</c> tags, so
/// detection continues without name enrichment (never silently: the warning
/// tells the operator exactly what is missing and where).
/// </summary>
public sealed class AttackTechniqueStore
{
    private readonly ILogger<AttackTechniqueStore>? _logger;
    private readonly string _stixFilePath;
    private readonly object _loadLock = new();
    private Dictionary<string, AttackTechnique>? _techniques;
    private bool _loadAttempted;

    public AttackTechniqueStore(AttackIntelOptions options, ILogger<AttackTechniqueStore>? logger = null)
    {
        _stixFilePath = options.StixFilePath;
        _logger = logger;
    }

    /// <summary>Techniques by ID (T1059, T1059.001 — normalized uppercase).</summary>
    public IReadOnlyDictionary<string, AttackTechnique> Techniques
    {
        get
        {
            EnsureLoaded();
            return _techniques!;
        }
    }

    /// <summary>
    /// Looks a technique up by ID; falls back to the parent technique for
    /// sub-techniques (T1059.001 → T1059) when the bundle has no sub-technique
    /// entry.
    /// </summary>
    public AttackTechnique? Find(string techniqueId)
    {
        var techniques = Techniques;
        if (techniques.TryGetValue(techniqueId.ToUpperInvariant(), out var technique))
        {
            return technique;
        }

        var dotIndex = techniqueId.IndexOf('.');
        if (dotIndex > 0)
        {
            var parent = techniqueId[..dotIndex].ToUpperInvariant();
            return techniques.TryGetValue(parent, out var parentTechnique) ? parentTechnique : null;
        }

        return null;
    }

    private void EnsureLoaded()
    {
        if (_techniques is not null)
        {
            return;
        }

        lock (_loadLock)
        {
            if (_techniques is not null || _loadAttempted)
            {
                return;
            }

            _loadAttempted = true;
            try
            {
                if (!File.Exists(_stixFilePath))
                {
                    _logger?.LogWarning(
                        "ATT&CK STIX bundle not found at {Path} — detection alerts carry raw technique IDs only. " +
                        "Drop an enterprise-attack.json STIX bundle there to enable name/tactic enrichment.",
                        _stixFilePath);
                    _techniques = new Dictionary<string, AttackTechnique>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                _techniques = LoadStixBundle(File.ReadAllText(_stixFilePath));
                _logger?.LogInformation(
                    "Loaded {Count} MITRE ATT&CK techniques from {Path}.",
                    _techniques.Count, _stixFilePath);
            }
            catch (Exception ex)
            {
                // A broken intel file must not take detection down; log the
                // reason and continue with raw IDs (no silent failure).
                _logger?.LogError(ex,
                    "Failed to parse ATT&CK STIX bundle at {Path}: {Reason} — detection alerts carry raw technique IDs only.",
                    _stixFilePath, ex.Message);
                _techniques = new Dictionary<string, AttackTechnique>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    internal Dictionary<string, AttackTechnique> LoadStixBundle(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("STIX bundle has no 'objects' array.");
        }

        var techniques = new Dictionary<string, AttackTechnique>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in objects.EnumerateArray())
        {
            if (!obj.TryGetProperty("type", out var type) ||
                !type.ValueEquals("attack-pattern") ||
                !obj.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string? externalId = null;
            if (obj.TryGetProperty("external_references", out var references) &&
                references.ValueKind == JsonValueKind.Array)
            {
                foreach (var reference in references.EnumerateArray())
                {
                    if (reference.TryGetProperty("source_name", out var sourceName) &&
                        sourceName.ValueEquals("mitre-attack") &&
                        reference.TryGetProperty("external_id", out var externalIdElement) &&
                        externalIdElement.ValueKind == JsonValueKind.String)
                    {
                        externalId = externalIdElement.GetString();
                        break;
                    }
                }
            }

            if (externalId is null || !externalId.StartsWith('T'))
            {
                continue;
            }

            var tactics = new List<string>();
            if (obj.TryGetProperty("kill_chain_phases", out var phases) && phases.ValueKind == JsonValueKind.Array)
            {
                foreach (var phase in phases.EnumerateArray())
                {
                    if (phase.TryGetProperty("kill_chain_name", out var chainName) &&
                        chainName.ValueEquals("mitre-attack") &&
                        phase.TryGetProperty("phase_name", out var phaseName) &&
                        phaseName.ValueKind == JsonValueKind.String)
                    {
                        tactics.Add(phaseName.GetString()!);
                    }
                }
            }

            var id = externalId.ToUpperInvariant();
            techniques[id] = new AttackTechnique(
                Id: id,
                Name: nameElement.GetString()!,
                Tactics: tactics,
                Url: $"https://attack.mitre.org/techniques/{id.Replace('.', '/')}/");
        }

        return techniques;
    }

    /// <summary>
    /// Parses a Sigma tag into its ATT&amp;CK meaning:
    /// <c>attack.t1059.001</c> → technique T1059.001, <c>attack.execution</c> →
    /// tactic execution. Other ATT&amp;CK tag namespaces (software <c>s…</c>,
    /// groups <c>g…</c>, mitigations <c>m…</c>) and unrelated tags return
    /// (null, null).
    /// </summary>
    public static (string? TechniqueId, string? Tactic) ParseTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return (null, null);
        }

        var normalized = tag.Trim().ToLowerInvariant();
        if (!normalized.StartsWith("attack.", StringComparison.Ordinal))
        {
            return (null, null);
        }

        var value = normalized["attack.".Length..];
        if (value.Length == 0)
        {
            return (null, null);
        }

        if (value[0] == 't' && value.Length > 1 && value.AsSpan(1).IndexOfAnyExcept("0123456789.") < 0)
        {
            return (value.ToUpperInvariant(), null);
        }

        // Tactic names are lowercase words; anything else (software s0xxx,
        // groups g0xxx, data sources ds0xxx, …) is not carried on alerts.
        return value.All(char.IsLetter) ? (null, value) : (null, null);
    }
}



