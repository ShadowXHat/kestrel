using System.Globalization;
using System.Text;
using Kestrel.Core.Models;

namespace Kestrel.Tests.Detection;

/// <summary>Shared builders for the Phase 5 (Sigma detection) tests.</summary>
internal static class DetectionFixtures
{
    public static readonly DateTimeOffset EventTime =
        DateTimeOffset.Parse("2026-03-01T12:00:00.0000000Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Raw event XML: System header plus the supplied EventData name/value pairs.</summary>
    public static string EventXml(long eventId, string channel, params (string Name, string Value)[] data)
    {
        var builder = new StringBuilder();
        builder.Append("<Event><System><Provider Name=\"Microsoft-Windows-Security-Auditing\"/>")
            .Append("<EventID>").Append(eventId.ToString(CultureInfo.InvariantCulture)).Append("</EventID>")
            .Append("<Channel>").Append(channel).Append("</Channel>")
            .Append("<Computer>WORKSTATION1</Computer></System><EventData>");

        foreach (var (name, value) in data)
        {
            builder.Append("<Data Name=\"").Append(name).Append("\">").Append(value).Append("</Data>");
        }

        builder.Append("</EventData></Event>");
        return builder.ToString();
    }

    public static StoredEvent MakeEvent(
        long eventId,
        string channel = "Security",
        string? xml = null,
        byte level = 4,
        string provider = "Microsoft-Windows-Security-Auditing",
        string computer = "WORKSTATION1",
        long? recordId = null) => new(
        RecordId: recordId ?? eventId,
        Channel: channel,
        EventId: eventId,
        Provider: provider,
        Level: level,
        LevelText: EventLevels.ToText(level),
        TimeCreatedUtc: EventTime,
        Computer: computer,
        UserSid: "S-1-5-18",
        Keywords: null,
        CorrelationId: null,
        ThreadId: 111,
        ProcessId: 222,
        Xml: xml ?? EventXml(eventId, channel),
        IngestedAtUtc: EventTime);

    /// <summary>Creates a unique temp directory (deleted by the owning test).</summary>
    public static string NewTempDirectory(string prefix)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "kestrel-tests", $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Writes a rule pack (nested directories included) and returns the pack directory.</summary>
    public static string WriteRulePack(params (string FileName, string Yaml)[] rules)
    {
        var directory = NewTempDirectory("rules");
        foreach (var (fileName, yaml) in rules)
        {
            var path = Path.Combine(directory, fileName);
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(path, yaml);
        }

        return directory;
    }

    /// <summary>Minimal ATT&amp;CK STIX bundle with one sub-technique, for enrichment tests.</summary>
    public static string WriteStixBundle(string directory)
    {
        var json = """
            {
              "type": "bundle",
              "objects": [
                {
                  "type": "attack-pattern",
                  "name": "Command and Scripting Interpreter: PowerShell",
                  "external_references": [
                    { "source_name": "mitre-attack", "external_id": "T1059.001" }
                  ],
                  "kill_chain_phases": [
                    { "kill_chain_name": "mitre-attack", "phase_name": "execution" }
                  ]
                }
              ]
            }
            """;

        var path = Path.Combine(directory, "enterprise-attack.json");
        File.WriteAllText(path, json);
        return path;
    }
}
