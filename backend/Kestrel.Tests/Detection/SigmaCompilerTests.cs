using Kestrel.Core.Detection;
using Xunit;

namespace Kestrel.Tests.Detection;

public sealed class SigmaCompilerTests
{
    [Fact]
    public void CompileYaml_MapsFieldsAndModifiersToExecutableRule()
    {
        const string yaml = """
            title: Encoded PowerShell
            id: 11111111-1111-1111-1111-111111111111
            status: stable
            level: high
            logsource:
              product: windows
              service: powershell
            tags:
              - attack.execution
              - attack.t1059.001
            detection:
              selection:
                Channel|nocase: microsoft-windows-powershell/operational
                EventID: 4104
                ScriptBlockText|contains|nocase: encodedcommand
                Provider|re: '^Microsoft-Windows-.*PowerShell$'
              condition: selection
            """;

        var compiled = new SigmaCompiler().CompileYaml(yaml, "encoded.yml");

        Assert.Equal("Encoded PowerShell", compiled.Rule.Title);
        Assert.Equal("encoded.yml", compiled.SourceFile);
        Assert.Equal(["T1059.001"], compiled.TechniqueIds);
        Assert.Contains("e.event_id", compiled.WhereClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("xml_field", compiled.WhereClause, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REGEXP", compiled.WhereClause, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(compiled.Prefilter.Channels);
        Assert.NotNull(compiled.Prefilter.EventIds);
        Assert.Contains("Microsoft-Windows-PowerShell/Operational", compiled.Prefilter.Channels!);
        Assert.Contains(4104L, compiled.Prefilter.EventIds!);

        var matching = DetectionFixtures.MakeEvent(
            4104,
            channel: "Microsoft-Windows-PowerShell/Operational",
            xml: DetectionFixtures.EventXml(
                4104,
                "Microsoft-Windows-PowerShell/Operational",
                ("ScriptBlockText", "Invoke-Mimikatz -EncodedCommand")));
        var nonMatching = DetectionFixtures.MakeEvent(
            4103,
            channel: "Microsoft-Windows-PowerShell/Operational",
            xml: DetectionFixtures.EventXml(
                4103,
                "Microsoft-Windows-PowerShell/Operational",
                ("ScriptBlockText", "Get-Process")));

        Assert.True(compiled.Evaluate(matching, out var matchedSelectors));
        Assert.Contains("selection", matchedSelectors);
        Assert.False(compiled.Evaluate(nonMatching, out _));
    }

    [Fact]
    public void CompileYaml_EvaluatesAndOrNotCondition()
    {
        const string yaml = """
            title: Suspicious Security Event
            level: medium
            detection:
              channel:
                Channel: Security
              logon:
                EventID: 4624
              failure:
                EventID: 4625
              condition: channel and (logon or not failure)
            """;

        var compiled = new SigmaCompiler().CompileYaml(yaml);

        Assert.True(compiled.Evaluate(DetectionFixtures.MakeEvent(4624), out _));
        Assert.True(compiled.Evaluate(DetectionFixtures.MakeEvent(1000), out _));
        Assert.False(compiled.Evaluate(DetectionFixtures.MakeEvent(4625), out _));
        Assert.False(compiled.Evaluate(
            DetectionFixtures.MakeEvent(4624, channel: "System"), out _));
    }

    [Fact]
    public void CompileYaml_LevelTextModifiersMatchBatchPrefilterSemantics()
    {
        const string yaml = """
            title: Warning level
            level: low
            detection:
              selection:
                Level|contains: warn
              condition: selection
            """;

        var compiled = new SigmaCompiler().CompileYaml(yaml);
        var storedEvent = DetectionFixtures.MakeEvent(1) with { LevelText = "Warning" };

        Assert.Contains("e.level_text", compiled.WhereClause, StringComparison.OrdinalIgnoreCase);
        Assert.True(compiled.Evaluate(storedEvent, out _));
        Assert.True(compiled.Prefilter.CouldMatch(storedEvent.Channel, storedEvent.EventId));
    }

    [Fact]
    public void CompileYaml_ChannelPrefilterIsCaseInsensitive()
    {
        const string yaml = """
            title: Security channel
            level: low
            detection:
              selection:
                Channel: Security
              condition: selection
            """;

        var compiled = new SigmaCompiler().CompileYaml(yaml);

        Assert.True(compiled.Prefilter.CouldMatch("security", 1));
        Assert.True(compiled.Evaluate(
            DetectionFixtures.MakeEvent(1, channel: "security"), out _));
    }

    [Fact]
    public void Parse_RejectsMalformedLogSourceShape()
    {
        const string yaml = """
            title: Invalid logsource
            level: low
            logsource: windows
            detection:
              selection:
                EventID: 1
              condition: selection
            """;

        var exception = Assert.Throws<SigmaParseException>(() => new SigmaCompiler().Parse(yaml));

        Assert.Contains("logsource", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompileYaml_RejectsUnsupportedModifierWithDiagnostic()
    {
        const string yaml = """
            title: Invalid rule
            level: low
            detection:
              selection:
                CommandLine|unsupported: powershell
              condition: selection
            """;

        var exception = Assert.Throws<SigmaParseException>(() => new SigmaCompiler().CompileYaml(yaml));

        Assert.Contains("unsupported", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RulePack_ReportsBrokenFilesAndLoadsValidNestedRules()
    {
        var directory = DetectionFixtures.WriteRulePack(
            ("security/valid.yml", """
                title: Valid rule
                id: 22222222-2222-2222-2222-222222222222
                level: informational
                detection:
                  selection:
                    EventID: 1
                  condition: selection
                """),
            ("broken.yml", "title: Broken\nlevel: invalid\n"));

        try
        {
            var pack = new RulePack(new SigmaOptions { RulePackPath = directory });

            var rule = Assert.Single(pack.Current.Rules);
            Assert.Equal("Valid rule", rule.Rule.Title);
            var failure = Assert.Single(pack.Current.Failures);
            Assert.Equal("broken.yml", failure.File);
            Assert.Contains("unknown level", failure.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}