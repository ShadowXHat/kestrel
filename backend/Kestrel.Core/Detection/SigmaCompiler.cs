using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kestrel.Core.Models;
using Kestrel.Core.Storage;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Kestrel.Core.Detection;

/// <summary>Target a Sigma field name resolves to for matching.</summary>
internal enum SigmaFieldTarget
{
    Channel,
    EventId,
    Provider,
    Level,
    Computer,
    RecordId,
    Keywords,
    CorrelationId,
    ThreadId,
    UserSid,
    XmlData,
}

/// <summary>
/// Maps Sigma field names to normalized event-store columns. Only unambiguous
/// System-section fields are mapped; everything else (EventData fields such as
/// CommandLine, TargetImage, SubjectUserSid, and even Sysmon's EventData
/// ProcessId, which collides with the System ProcessID) extracts from the raw
/// event XML via <c>xml_field()</c> — see Storage/SqlFunctions.cs.
/// </summary>
internal static class SigmaFieldRegistry
{
    public static readonly Dictionary<string, SigmaFieldTarget> Targets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["channel"] = SigmaFieldTarget.Channel,
        ["eventid"] = SigmaFieldTarget.EventId,
        ["provider"] = SigmaFieldTarget.Provider,
        ["provider_name"] = SigmaFieldTarget.Provider,
        ["level"] = SigmaFieldTarget.Level,
        ["computer"] = SigmaFieldTarget.Computer,
        ["recordid"] = SigmaFieldTarget.RecordId,
        ["eventrecordid"] = SigmaFieldTarget.RecordId,
        ["keywords"] = SigmaFieldTarget.Keywords,
        ["correlationid"] = SigmaFieldTarget.CorrelationId,
        ["activityid"] = SigmaFieldTarget.CorrelationId,
        ["threadid"] = SigmaFieldTarget.ThreadId,
        ["usersid"] = SigmaFieldTarget.UserSid,
    };

    public static SigmaFieldTarget Resolve(string field) =>
        Targets.TryGetValue(field, out var target) ? target : SigmaFieldTarget.XmlData;
}

/// <summary>Value-pattern translation between Sigma semantics, SQL LIKE and .NET regex.</summary>
internal static class SigmaPatterns
{
    public static bool HasWildcard(string value) => value.Contains('*') || value.Contains('?');

    public static string ToLikePattern(string value, SigmaMatchMode mode)
    {
        var sb = new StringBuilder();
        if (mode is SigmaMatchMode.Contains or SigmaMatchMode.EndsWith)
        {
            sb.Append('%');
        }

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '*': sb.Append('%'); break;
                case '?': sb.Append('_'); break;
                case '\\': sb.Append("\\\\"); break;
                case '%': sb.Append("\\%"); break;
                case '_': sb.Append("\\_"); break;
                default: sb.Append(ch); break;
            }
        }

        if (mode is SigmaMatchMode.Contains or SigmaMatchMode.StartsWith)
        {
            sb.Append('%');
        }

        return sb.ToString();
    }

    public static string ToRegexPattern(string value, SigmaMatchMode mode, bool ignoreCase)
    {
        var sb = new StringBuilder();
        if (ignoreCase)
        {
            sb.Append("(?i)");
        }

        sb.Append('^');
        if (mode is SigmaMatchMode.Contains or SigmaMatchMode.EndsWith)
        {
            sb.Append(".*");
        }

        foreach (var ch in value)
        {
            if (ch == '*')
            {
                sb.Append(".*");
            }
            else if (ch == '?')
            {
                sb.Append('.');
            }
            else
            {
                sb.Append(Regex.Escape(ch.ToString()));
            }
        }

        if (mode is SigmaMatchMode.Contains or SigmaMatchMode.StartsWith)
        {
            sb.Append(".*");
        }

        sb.Append('$');
        return sb.ToString();
    }
}

/// <summary>Accumulates parameterized SQL fragments for one compiled rule.</summary>
internal sealed class SigmaSqlComposer
{
    public List<(string Name, object? Value)> Parameters { get; } = [];

    public string AddParameter(object? value)
    {
        var name = $"@p{Parameters.Count}";
        Parameters.Add((name, value));
        return name;
    }

    /// <summary>
    /// Wraps a comparison so an absent field (SQL NULL) is a definite "false",
    /// matching in-memory semantics and making NOT/AND/OR behave like plain
    /// boolean logic instead of SQL three-valued logic.
    /// </summary>
    public static string Coalesce(string booleanExpression) => $"COALESCE(({booleanExpression}), 0)";
}

/// <summary>
/// Compiles Sigma rules (v1.0 YAML) into executable form: a parameterized SQL
/// WHERE clause for set-based scans over the SQLite events table, and an
/// in-memory evaluator for live batches — plus a sound channel + EventID
/// pre-filter for cheap rejection (.clinerules: channel+EventID pre-filter →
/// FastMatch → expression evaluation).
///
/// All literal values are bound as SQL parameters — rule YAML is never
/// interpolated into SQL text.
/// </summary>
public sealed class SigmaCompiler
{
    /// <summary>Selector names that collide with condition keywords.</summary>
    private static readonly HashSet<string> ReservedSelectorNames =
        new(["and", "or", "not", "all", "of", "them"], StringComparer.OrdinalIgnoreCase);

    /// <summary>logsource.service → Windows channel (product: windows). Unknown services do not restrict.</summary>
    private static readonly Dictionary<string, string> WindowsServiceChannels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["security"] = "Security",
            ["ntlm"] = "Security",
            ["sysmon"] = "Microsoft-Windows-Sysmon/Operational",
            ["system"] = "System",
            ["application"] = "Application",
            ["powershell"] = "Microsoft-Windows-PowerShell/Operational",
            ["powershell-classic"] = "Windows PowerShell",
            ["wmi"] = "Microsoft-Windows-WMI/Operational",
            ["windefend"] = "Microsoft-Windows-Windows Defender/Operational",
            ["defender"] = "Microsoft-Windows-Windows Defender/Operational",
            ["bits-client"] = "Microsoft-Windows-Bits-Client/Operational",
            ["codeintegrity"] = "Microsoft-Windows-CodeIntegrity/Operational",
            ["taskscheduler"] = "Microsoft-Windows-TaskScheduler/Operational",
            ["driver-framework"] = "Microsoft-Windows-DriverFrameworks-User/Operational",
            ["terminalservices-localsessionmanager"] =
                "Microsoft-Windows-TerminalServices-LocalSessionManager/Operational",
            ["winrm"] = "Microsoft-Windows-WinRM/Operational",
            ["firewall-as"] = "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall",
            ["dns-server"] = "DNS Server",
            ["security-mitigations"] = "Microsoft-Windows-Security-Mitigations/Operational",
            ["shell-core"] = "Microsoft-Windows-Shell-Core/Operational",
            ["printservice-admin"] = "PrintService/Admin",
            ["printservice-operational"] = "PrintService/Operational",
            ["smbclient-security"] = "Microsoft-Windows-SMBClient/Security",
            ["smbserver-security"] = "Microsoft-Windows-SMBServer/Security",
        };

    // ======================================================================
    // Parsing (Sigma v1.0 YAML → SigmaRule)
    // ======================================================================

    public SigmaRule Parse(string yaml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yaml);

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new SigmaParseException($"Invalid YAML: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            throw new SigmaParseException("YAML input contains no document.");
        }

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new SigmaParseException("Sigma rule root must be a YAML mapping.");
        }

        var title = ScalarOrNull(root, "title") ?? throw new SigmaParseException("Sigma rule is missing 'title'.");
        var detectionNode = Child(root, "detection") as YamlMappingNode
            ?? throw new SigmaParseException("Sigma rule is missing the 'detection' mapping.");

        // Pass 1: raw selector entries (document order), condition + timeframe.
        var rawSelectors = new List<(string Name, YamlNode Node)>();
        YamlNode? conditionNode = null;
        string? timeframe = null;
        foreach (var (keyNode, valueNode) in detectionNode.Children)
        {
            if (keyNode is not YamlScalarNode scalar || scalar.Value is null)
            {
                throw new SigmaParseException("detection keys must be scalar selector names.");
            }

            var key = scalar.Value;
            if (key.Equals("condition", StringComparison.OrdinalIgnoreCase))
            {
                conditionNode = valueNode;
            }
            else if (key.Equals("timeframe", StringComparison.OrdinalIgnoreCase))
            {
                timeframe = (valueNode as YamlScalarNode)?.Value;
            }
            else
            {
                rawSelectors.Add((key, valueNode));
            }
        }

        if (rawSelectors.Count == 0)
        {
            throw new SigmaParseException("detection contains no selectors.");
        }

        var names = rawSelectors.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);

        // Pass 2: classify each selector by its YAML shape.
        var selectors = new Dictionary<string, SigmaSelector>(StringComparer.Ordinal);
        foreach (var (name, node) in rawSelectors)
        {
            selectors[name] = ParseSelector(name, node, names);
        }

        if (conditionNode is null)
        {
            throw new SigmaParseException("detection is missing 'condition'.");
        }

        var condition = conditionNode switch
        {
            YamlScalarNode s when s.Value is not null => ParseConditionString(s.Value, selectors),
            YamlSequenceNode list => ParseConditionList(list, selectors),
            _ => throw new SigmaParseException("condition must be a string (or list of strings)."),
        };

        return new SigmaRule(
            Title: title.Trim(),
            Id: ScalarOrNull(root, "id"),
            Status: ParseEnum<SigmaStatus>(root, "status", "status") ?? SigmaStatus.Undefined,
            Description: ScalarOrNull(root, "description"),
            Author: ScalarOrNull(root, "author"),
            License: ScalarOrNull(root, "license"),
            References: StringListOrNull(root, "references") ?? [],
            Date: ScalarOrNull(root, "date"),
            LogSource: ParseLogSource(root),
            Detection: new SigmaDetection(selectors, condition, timeframe),
            Level: ParseEnum<SigmaSeverity>(root, "level", "level")
                ?? throw new SigmaParseException("Sigma rule is missing 'level'."),
            Tags: StringListOrNull(root, "tags") ?? [],
            FalsePositives: StringListOrNull(root, "falsepositives") ?? []);
    }

    private static SigmaSelector ParseSelector(string name, YamlNode node, HashSet<string> allNames)
    {
        switch (node)
        {
            case YamlMappingNode map:
                return ParseFieldSelector(name, map);
            case YamlSequenceNode list:
                return ParseListSelector(name, list, allNames);
            case YamlScalarNode scalar:
            {
                var keyword = ScalarValue(scalar);
                return keyword is null
                    ? throw new SigmaParseException($"selector '{name}' has an empty value.")
                    : new SigmaKeywordSelector(name, [keyword]);
            }
            default:
                throw new SigmaParseException($"selector '{name}' has an unsupported YAML shape.");
        }
    }

    private static SigmaFieldSelector ParseFieldSelector(string name, YamlMappingNode map)
    {
        var fields = new List<SigmaFieldMatch>();
        foreach (var (keyNode, valueNode) in map.Children)
        {
            if (keyNode is not YamlScalarNode keyScalar || string.IsNullOrWhiteSpace(keyScalar.Value))
            {
                throw new SigmaParseException($"selector '{name}' contains a non-scalar field name.");
            }

            fields.Add(ParseFieldMatch(name, keyScalar.Value, valueNode));
        }

        if (fields.Count == 0)
        {
            throw new SigmaParseException($"selector '{name}' is empty.");
        }

        return new SigmaFieldSelector(name, [new SigmaFieldGroup(fields)]);
    }

    private static SigmaSelector ParseListSelector(string name, YamlSequenceNode list, HashSet<string> allNames)
    {
        var items = list.Children.ToList();
        if (items.Count == 0)
        {
            throw new SigmaParseException($"selector '{name}' is an empty list.");
        }

        // A list of maps: each map is an OR'd alternative (Sigma list-of-maps).
        if (items.All(i => i is YamlMappingNode))
        {
            var groups = items
                .Cast<YamlMappingNode>()
                .Select(m => ParseFieldSelector(name, m))
                .SelectMany(g => g.Groups)
                .ToList();
            return new SigmaFieldSelector(name, groups);
        }

        if (items.Any(i => i is not YamlScalarNode))
        {
            throw new SigmaParseException($"selector '{name}': lists must contain only scalars or only maps.");
        }

        var values = items
            .Cast<YamlScalarNode>()
            .Select(ScalarValue)
            .Select(v => v ?? throw new SigmaParseException($"selector '{name}' contains an empty value."))
            .ToList();

        // A list whose scalars name other selectors is a reference list
        // (implicit OR of the referenced selectors), not a keyword list.
        if (values.All(v => allNames.Contains(v) && !v.Equals(name, StringComparison.Ordinal)))
        {
            return new SigmaNameListSelector(name, values);
        }

        return new SigmaKeywordSelector(name, values);
    }

    private static SigmaFieldMatch ParseFieldMatch(string selectorName, string key, YamlNode valueNode)
    {
        var parts = key.Split('|');
        var field = parts[0].Trim();
        if (field.Length == 0)
        {
            throw new SigmaParseException($"selector '{selectorName}' contains a field with an empty name.");
        }

        var mode = SigmaMatchMode.Wildcard;
        var modeSet = false;
        var noCase = false;
        var allValues = false;

        foreach (var raw in parts.Skip(1))
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "contains": SetMode(SigmaMatchMode.Contains); break;
                case "startswith": SetMode(SigmaMatchMode.StartsWith); break;
                case "endswith": SetMode(SigmaMatchMode.EndsWith); break;
                case "re": SetMode(SigmaMatchMode.Regex); break;
                case "gt": SetMode(SigmaMatchMode.GreaterThan); break;
                case "gte": SetMode(SigmaMatchMode.GreaterOrEqual); break;
                case "lt": SetMode(SigmaMatchMode.LessThan); break;
                case "lte": SetMode(SigmaMatchMode.LessOrEqual); break;
                case "nocase": noCase = true; break;
                case "all": allValues = true; break;
                default:
                    throw new SigmaParseException(
                        $"unknown or unsupported modifier '{raw.Trim()}' on field '{field}' " +
                        "(supported: contains, startswith, endswith, re, gt, gte, lt, lte, nocase, all).");
            }
        }

        void SetMode(SigmaMatchMode newMode)
        {
            if (modeSet)
            {
                throw new SigmaParseException(
                    $"field '{field}' combines multiple comparison modifiers — use exactly one of " +
                    "contains/startswith/endswith/re/gt/gte/lt/lte.");
            }

            mode = newMode;
            modeSet = true;
        }

        var (isNull, values) = ParseValues(selectorName, field, valueNode);
        return new SigmaFieldMatch(field, isNull, values, allValues, mode, noCase);
    }

    private static (bool IsNull, List<string> Values) ParseValues(
        string selectorName, string field, YamlNode valueNode)
    {
        switch (valueNode)
        {
            case YamlScalarNode scalar when ScalarIsNull(scalar):
                return (true, []);
            case YamlScalarNode scalar:
                return (false, [scalar.Value!]);
            case YamlSequenceNode list:
            {
                var values = new List<string>();
                foreach (var item in list.Children)
                {
                    if (item is not YamlScalarNode scalar || ScalarIsNull(scalar))
                    {
                        throw new SigmaParseException(
                            $"field '{field}' in selector '{selectorName}': list values must be non-null scalars.");
                    }

                    values.Add(scalar.Value!);
                }

                return (false, values);
            }
            default:
                throw new SigmaParseException(
                    $"field '{field}' in selector '{selectorName}': value must be a scalar or list.");
        }
    }

    /// <summary>YAML null detection: empty scalar or a plain (unquoted) YAML null keyword.</summary>
    private static bool ScalarIsNull(YamlScalarNode scalar) =>
        scalar.Value is null ||
        (scalar.Style == ScalarStyle.Plain && scalar.Value is "" or "null" or "Null" or "NULL" or "~");

    private static string? ScalarValue(YamlScalarNode scalar) =>
        scalar.Value is null ? null : scalar.Value.Trim();

    private static YamlNode? Child(YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    private static string? ScalarOrNull(YamlMappingNode map, string key) =>
        Child(map, key) is YamlScalarNode scalar ? ScalarValue(scalar) : null;

    private static List<string>? StringListOrNull(YamlMappingNode map, string key)
    {
        var node = Child(map, key);
        return node switch
        {
            null => null,
            YamlScalarNode scalar => ScalarValue(scalar) is { } value ? [value] : null,
            YamlSequenceNode list => ParseStringSequence(key, list),
            _ => throw new SigmaParseException($"'{key}' must be a scalar or a list of scalars."),
        };
    }

    private static List<string> ParseStringSequence(string key, YamlSequenceNode list)
    {
        var values = new List<string>();
        foreach (var item in list.Children)
        {
            if (item is not YamlScalarNode scalar || ScalarIsNull(scalar) || ScalarValue(scalar) is not { } value)
            {
                throw new SigmaParseException($"'{key}' must contain only non-null scalar values.");
            }

            values.Add(value);
        }

        return values;
    }

    private static T? ParseEnum<T>(YamlMappingNode map, string key, string label) where T : struct, Enum
    {
        var raw = ScalarOrNull(map, key);
        if (raw is null)
        {
            return null;
        }

        if (Enum.TryParse<T>(raw.Replace("-", "").Replace("_", ""), true, out var parsed) &&
            Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new SigmaParseException(
            $"unknown {label} '{raw}' (accepted: {string.Join(", ", Enum.GetNames<T>())}).");
    }

    private static SigmaLogSource? ParseLogSource(YamlMappingNode root)
    {
        var node = Child(root, "logsource");
        if (node is null)
        {
            return null;
        }

        if (node is not YamlMappingNode logsource)
        {
            throw new SigmaParseException("'logsource' must be a mapping.");
        }

        return new SigmaLogSource(
            Category: ScalarOrNull(logsource, "category"),
            Product: ScalarOrNull(logsource, "product"),
            Service: ScalarOrNull(logsource, "service"),
            Definition: ScalarOrNull(logsource, "definition"));
    }

    // ======================================================================
    // Condition parsing (AND / OR / NOT / parentheses / "N of" quantifiers)
    // ======================================================================

    private SigmaConditionNode ParseConditionList(YamlSequenceNode list, IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        var terms = new List<SigmaConditionNode>();
        foreach (var item in list.Children)
        {
            if (item is not YamlScalarNode scalar || scalar.Value is null)
            {
                throw new SigmaParseException("condition list items must be strings.");
            }

            terms.Add(ParseConditionString(scalar.Value, selectors));
        }

        return terms.Count == 1 ? terms[0] : new SigmaOrCondition(terms);
    }

    private SigmaConditionNode ParseConditionString(string text, IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SigmaParseException("condition is empty.");
        }

        var reader = new TokenReader(TokenizeCondition(text));
        var node = ParseOrExpression(reader, selectors);
        if (!reader.AtEnd)
        {
            throw new SigmaParseException($"condition has unexpected token '{reader.Peek().Text}'.");
        }

        return node;
    }

    private sealed record ConditionToken(ConditionTokenKind Kind, string Text);

    private enum ConditionTokenKind { Name, Number, LeftParen, RightParen }

    private static List<ConditionToken> TokenizeCondition(string text)
    {
        var tokens = new List<ConditionToken>();
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (char.IsWhiteSpace(ch))
            {
                index++;
                continue;
            }

            if (ch == '(')
            {
                tokens.Add(new ConditionToken(ConditionTokenKind.LeftParen, "("));
                index++;
                continue;
            }

            if (ch == ')')
            {
                tokens.Add(new ConditionToken(ConditionTokenKind.RightParen, ")"));
                index++;
                continue;
            }

            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' or '*')
            {
                var start = index;
                while (index < text.Length &&
                       (char.IsLetterOrDigit(text[index]) || text[index] is '_' or '-' or '.' or '*'))
                {
                    index++;
                }

                var word = text[start..index];
                tokens.Add(char.IsDigit(word[0]) && word.All(char.IsDigit)
                    ? new ConditionToken(ConditionTokenKind.Number, word)
                    : new ConditionToken(ConditionTokenKind.Name, word));
                continue;
            }

            throw new SigmaParseException($"condition contains an unexpected character '{ch}'.");
        }

        return tokens;
    }

    private sealed class TokenReader(List<ConditionToken> tokens)
    {
        private int _position;

        public bool AtEnd => _position >= tokens.Count;

        public ConditionToken Peek() => _position < tokens.Count
            ? tokens[_position]
            : throw new SigmaParseException("condition ended unexpectedly.");

        public ConditionToken Take() => _position < tokens.Count
            ? tokens[_position++]
            : throw new SigmaParseException("condition ended unexpectedly.");

        public bool TryTakeKeyword(string keyword, out ConditionToken token)
        {
            if (!AtEnd && Peek().Kind == ConditionTokenKind.Name &&
                Peek().Text.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                token = Take();
                return true;
            }

            token = null!;
            return false;
        }

        public bool TryTake(ConditionTokenKind kind, out ConditionToken token)
        {
            if (!AtEnd && Peek().Kind == kind)
            {
                token = Take();
                return true;
            }

            token = null!;
            return false;
        }
    }

    private SigmaConditionNode ParseOrExpression(TokenReader reader, IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        var terms = new List<SigmaConditionNode> { ParseAndExpression(reader, selectors) };
        while (reader.TryTakeKeyword("or", out _))
        {
            terms.Add(ParseAndExpression(reader, selectors));
        }

        return terms.Count == 1 ? terms[0] : new SigmaOrCondition(terms);
    }

    private SigmaConditionNode ParseAndExpression(TokenReader reader, IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        var terms = new List<SigmaConditionNode> { ParseUnaryExpression(reader, selectors) };
        while (reader.TryTakeKeyword("and", out _))
        {
            terms.Add(ParseUnaryExpression(reader, selectors));
        }

        return terms.Count == 1 ? terms[0] : new SigmaAndCondition(terms);
    }

    private SigmaConditionNode ParseUnaryExpression(TokenReader reader, IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        if (reader.TryTakeKeyword("not", out _))
        {
            return new SigmaNotCondition(ParseUnaryExpression(reader, selectors));
        }

        return ParseAtom(reader, selectors);
    }

    private SigmaConditionNode ParseAtom(TokenReader reader, IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        if (reader.TryTake(ConditionTokenKind.LeftParen, out var paren))
        {
            var inner = ParseOrExpression(reader, selectors);
            if (!reader.TryTake(ConditionTokenKind.RightParen, out _))
            {
                throw new SigmaParseException($"condition parenthesis opened at '{paren.Text}' is never closed.");
            }

            return inner;
        }

        var token = reader.Take();
        switch (token.Kind)
        {
            case ConditionTokenKind.Number:
            {
                var count = int.Parse(token.Text, CultureInfo.InvariantCulture);
                return ParseOfTail(reader, selectors, count, isAll: false);
            }
            case ConditionTokenKind.Name when token.Text.Equals("all", StringComparison.OrdinalIgnoreCase):
                return ParseOfTail(reader, selectors, count: 0, isAll: true);
            case ConditionTokenKind.Name when ReservedSelectorNames.Contains(token.Text):
                throw new SigmaParseException($"condition keyword '{token.Text}' is reserved.");
            case ConditionTokenKind.Name:
                return new SigmaSelectorCondition(token.Text);
            default:
                throw new SigmaParseException($"unexpected condition token '{token.Text}'.");
        }
    }

    /// <summary>Parses "of (them | name | name*)" after the count / 'all' keyword.</summary>
    private SigmaConditionNode ParseOfTail(
        TokenReader reader,
        IReadOnlyDictionary<string, SigmaSelector> selectors,
        int count,
        bool isAll)
    {
        if (!reader.TryTakeKeyword("of", out _))
        {
            throw new SigmaParseException(isAll
                ? "'all' must be followed by 'of'."
                : $"number '{count}' in condition must be followed by 'of'.");
        }

        var pattern = reader.Take();
        if (pattern.Kind != ConditionTokenKind.Name)
        {
            throw new SigmaParseException("'of' must be followed by 'them' or a selector name.");
        }

        if (pattern.Text.Equals("them", StringComparison.OrdinalIgnoreCase))
        {
            var terms = selectors.Keys.OrderBy(k => k, StringComparer.Ordinal)
                .Select(name => (SigmaConditionNode)new SigmaSelectorCondition(name))
                .ToList();
            return BuildOf(count, isAll, terms, "them");
        }

        if (pattern.Text.EndsWith('*'))
        {
            var prefix = pattern.Text[..^1];
            var terms = selectors.Keys
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(k => k, StringComparer.Ordinal)
                .Select(name => (SigmaConditionNode)new SigmaSelectorCondition(name))
                .ToList();
            return BuildOf(count, isAll, terms, $"'{pattern.Text}'");
        }

        if (!selectors.ContainsKey(pattern.Text))
        {
            throw new SigmaParseException($"'of' references unknown selector '{pattern.Text}'.");
        }

        return BuildOf(count, isAll, [new SigmaSelectorCondition(pattern.Text)], $"'{pattern.Text}'");
    }

    private static SigmaConditionNode BuildOf(int count, bool isAll, List<SigmaConditionNode> terms, string source)
    {
        if (terms.Count == 0)
        {
            throw new SigmaParseException($"condition 'of {source}' matches no selectors.");
        }

        if (isAll)
        {
            return new SigmaOfCondition(terms.Count, IsAll: true, terms);
        }

        if (count < 1)
        {
            throw new SigmaParseException("'N of' requires N >= 1.");
        }

        if (count > terms.Count)
        {
            throw new SigmaParseException(
                $"condition 'of {source}' requires {count} matching selectors but only expands to {terms.Count}.");
        }

        return new SigmaOfCondition(count, IsAll: false, terms);
    }

    // ======================================================================
    // Validation (blocking errors vs non-blocking warnings)
    // ======================================================================

    public IReadOnlyList<SigmaValidationIssue> Validate(SigmaRule rule)
    {
        var issues = new List<SigmaValidationIssue>();
        var detection = rule.Detection;

        if (string.IsNullOrWhiteSpace(rule.Title))
        {
            issues.Add(Err("missing_title", "rule title is required."));
        }

        if (rule.LogSource is null)
        {
            issues.Add(new SigmaValidationIssue(SigmaIssueSeverity.Warning, "missing_logsource",
                "rule has no logsource block — channel pre-filtering is disabled for it."));
        }

        if (rule.Status is SigmaStatus.Deprecated or SigmaStatus.Unsupported)
        {
            issues.Add(new SigmaValidationIssue(SigmaIssueSeverity.Warning, "rule_deprecated",
                $"rule status is '{rule.Status}'."));
        }

        if (rule.Id is { Length: > 0 } id && !Guid.TryParse(id, out _))
        {
            issues.Add(new SigmaValidationIssue(SigmaIssueSeverity.Warning, "id_not_uuid",
                $"id '{id}' is not a UUID (Sigma spec expects one)."));
        }

        if (detection.Timeframe is not null)
        {
            issues.Add(new SigmaValidationIssue(SigmaIssueSeverity.Warning, "timeframe_unenforced",
                "timeframe is recorded but not enforced — event correlation is out of scope for Phase 5."));
        }

        foreach (var selector in detection.Selectors.Values)
        {
            if (ReservedSelectorNames.Contains(selector.Name))
            {
                issues.Add(Err("reserved_selector_name",
                    $"selector name '{selector.Name}' collides with a condition keyword."));
            }

            if (selector.Name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '-' or '.')))
            {
                issues.Add(Err("invalid_selector_name",
                    $"selector name '{selector.Name}' contains characters outside [A-Za-z0-9_.-]."));
            }
        }

        // Every selector referenced by the condition (and by reference lists) must exist.
        CheckConditionReferences(detection.Condition, detection.Selectors, issues);

        // Reference-list selectors: members exist, and the reference graph is acyclic.
        foreach (var listSelector in detection.Selectors.Values.OfType<SigmaNameListSelector>())
        {
            if (listSelector.ReferencedSelectors.Count == 0)
            {
                issues.Add(Err("empty_reference_list", $"selector '{listSelector.Name}' references no selectors."));
            }

            foreach (var reference in listSelector.ReferencedSelectors)
            {
                if (!detection.Selectors.ContainsKey(reference))
                {
                    issues.Add(Err("unknown_selector_ref",
                        $"selector '{listSelector.Name}' references unknown selector '{reference}'."));
                }
            }
        }

        DetectReferenceCycles(detection.Selectors, issues);

        foreach (var fieldSelector in detection.Selectors.Values.OfType<SigmaFieldSelector>())
        foreach (var group in fieldSelector.Groups)
        foreach (var field in group.Fields)
        {
            ValidateFieldMatch(field, issues);
        }

        foreach (var keywordSelector in detection.Selectors.Values.OfType<SigmaKeywordSelector>()
                     .Where(k => k.Keywords.Count == 0))
        {
            issues.Add(Err("empty_keyword_selector", $"keyword selector '{keywordSelector.Name}' has no keywords."));
        }

        return issues;
    }

    private static void ValidateFieldMatch(SigmaFieldMatch field, List<SigmaValidationIssue> issues)
    {
        var target = SigmaFieldRegistry.Resolve(field.Field);

        if (field.IsNullValue)
        {
            if (field.Values.Count > 0)
            {
                issues.Add(Err("null_with_values", $"field '{field.Field}' cannot combine null with other values."));
            }

            if (field.Mode != SigmaMatchMode.Wildcard || field.AllValues)
            {
                issues.Add(Err("null_with_comparison", $"field '{field.Field}': null accepts no modifiers."));
            }

            return;
        }

        if (field.Values.Count == 0)
        {
            issues.Add(Err("empty_field_values", $"field '{field.Field}' has no values."));
            return;
        }

        if (field.AllValues && field.Values.Count == 1)
        {
            issues.Add(new SigmaValidationIssue(SigmaIssueSeverity.Warning, "all_with_single_value",
                $"field '{field.Field}': 'all' modifier with a single value is redundant."));
        }

        switch (target)
        {
            case SigmaFieldTarget.EventId or SigmaFieldTarget.RecordId or SigmaFieldTarget.Keywords
                or SigmaFieldTarget.ThreadId:
                ValidateNumericField(field, issues);
                break;

            case SigmaFieldTarget.Level:
                ValidateLevelField(field, issues);
                break;

            case SigmaFieldTarget.XmlData:
                if (IsComparisonMode(field.Mode))
                {
                    issues.Add(Err("comparison_on_xml_field",
                        $"field '{field.Field}' resolves to event XML — gt/gte/lt/lte comparisons are not supported; " +
                        "map it to a normalized column or use string matching."));
                }

                ValidateRegex(field, issues);
                break;

            default:
                if (IsComparisonMode(field.Mode))
                {
                    issues.Add(Err("comparison_on_text_field",
                        $"field '{field.Field}' is a text column — gt/gte/lt/lte are not supported."));
                }

                ValidateRegex(field, issues);
                break;
        }

        if (field.AllValues)
        {
            switch (target)
            {
                case SigmaFieldTarget.EventId or SigmaFieldTarget.RecordId or SigmaFieldTarget.Keywords
                    or SigmaFieldTarget.ThreadId when field.Values
                        .Select(v => TryParseLong(v, out var parsed) ? parsed : default(long?))
                        .Where(v => v is not null)
                        .Cast<long?>()
                        .Distinct()
                        .Count() > 1:
                    issues.Add(Err("all_with_distinct_numeric_values",
                        $"field '{field.Field}': 'all' with distinct numeric values can never match."));
                    break;

                case SigmaFieldTarget.Level when field.Values.Any(IsNumeric) && field.Values.Any(v => !IsNumeric(v)):
                    issues.Add(Err("mixed_level_values",
                        $"field '{field.Field}': 'all' cannot mix numeric and text level values."));
                    break;
            }
        }
    }

    private static void ValidateNumericField(SigmaFieldMatch field, List<SigmaValidationIssue> issues)
    {
        if (field.Mode is SigmaMatchMode.Regex or SigmaMatchMode.Contains or SigmaMatchMode.StartsWith
            or SigmaMatchMode.EndsWith)
        {
            issues.Add(Err("unsupported_mode_for_numeric",
                $"field '{field.Field}' is a numeric column — contains/startswith/endswith/re are not supported."));
        }

        if (IsComparisonMode(field.Mode) && field.Values.Count != 1)
        {
            issues.Add(Err("comparison_needs_single_value",
                $"field '{field.Field}': gt/gte/lt/lte require exactly one value."));
        }

        foreach (var value in field.Values)
        {
            if (SigmaPatterns.HasWildcard(value))
            {
                issues.Add(Err("wildcard_in_numeric",
                    $"field '{field.Field}': numeric comparison does not support wildcards ('{value}')."));
            }
            else if (!TryParseLong(value, out _))
            {
                issues.Add(Err("non_numeric_value",
                    $"field '{field.Field}': value '{value}' is not an integer."));
            }
        }
    }

    private static void ValidateLevelField(SigmaFieldMatch field, List<SigmaValidationIssue> issues)
    {
        if (IsComparisonMode(field.Mode))
        {
            if (field.Values.Count != 1)
            {
                issues.Add(Err("comparison_needs_single_value",
                    $"field '{field.Field}': gt/gte/lt/lte require exactly one value."));
            }

            foreach (var value in field.Values.Where(v => !IsNumeric(v)))
            {
                issues.Add(Err("non_numeric_value",
                    $"field '{field.Field}': numeric comparison requires integer values ('{value}')."));
            }
        }
        else
        {
            ValidateRegex(field, issues);
        }
    }

    private static void ValidateRegex(SigmaFieldMatch field, List<SigmaValidationIssue> issues)
    {
        if (field.Mode != SigmaMatchMode.Regex)
        {
            return;
        }

        foreach (var value in field.Values)
        {
            try
            {
                _ = new Regex(BuildRegexSource(value, field.NoCase), RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                issues.Add(Err("invalid_regex",
                    $"field '{field.Field}': regex '{value}' does not compile ({ex.Message})."));
            }
        }
    }

    private static void CheckConditionReferences(
        SigmaConditionNode node,
        IReadOnlyDictionary<string, SigmaSelector> selectors,
        List<SigmaValidationIssue> issues)
    {
        switch (node)
        {
            case SigmaSelectorCondition condition when !selectors.ContainsKey(condition.Name):
                issues.Add(Err("unknown_selector", $"condition references unknown selector '{condition.Name}'."));
                break;
            case SigmaAndCondition and:
                foreach (var term in and.Terms) CheckConditionReferences(term, selectors, issues);
                break;
            case SigmaOrCondition orCondition:
                foreach (var term in orCondition.Terms) CheckConditionReferences(term, selectors, issues);
                break;
            case SigmaNotCondition notCondition:
                CheckConditionReferences(notCondition.Inner, selectors, issues);
                break;
            case SigmaOfCondition ofCondition:
                foreach (var term in ofCondition.Terms) CheckConditionReferences(term, selectors, issues);
                break;
        }
    }

    private static void DetectReferenceCycles(
        IReadOnlyDictionary<string, SigmaSelector> selectors,
        List<SigmaValidationIssue> issues)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in selectors.Keys)
        {
            Visit(name);
        }

        void Visit(string current)
        {
            if (visited.Contains(current))
            {
                return;
            }

            if (!visiting.Add(current))
            {
                issues.Add(Err("cyclic_reference", $"selector reference cycle detected involving '{current}'."));
                return;
            }

            if (selectors.TryGetValue(current, out var selector) && selector is SigmaNameListSelector list)
            {
                foreach (var reference in list.ReferencedSelectors)
                {
                    if (selectors.ContainsKey(reference))
                    {
                        Visit(reference);
                    }
                }
            }

            visiting.Remove(current);
            visited.Add(current);
        }
    }

    // ======================================================================
    // Compilation (SQL + in-memory evaluator)
    // ======================================================================

    public CompiledSigmaRule Compile(SigmaRule rule, string? sourceFile = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var issues = Validate(rule);
        var errors = issues.Where(i => i.Severity == SigmaIssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new SigmaCompileException(errors);
        }

        var composer = new SigmaSqlComposer();

        // In-memory evaluators (FastMatch + condition evaluation).
        var selectorEvals = new Dictionary<string, SigmaSelectorEvaluator>(StringComparer.Ordinal);
        foreach (var name in rule.Detection.Selectors.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            selectorEvals[name] = BuildSelectorEval(
                rule.Detection.Selectors[name], selectorEvals, rule.Detection.Selectors);
        }

        var conditionEval = BuildConditionEval(rule.Detection.Condition, selectorEvals);

        // SQL WHERE clause (set-based scans over the events table).
        var selectorSql = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in rule.Detection.Selectors.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            selectorSql[name] = SelectorSql(
                rule.Detection.Selectors[name], selectorSql, rule.Detection.Selectors, composer);
        }

        var conditionSql = BuildConditionSql(rule.Detection.Condition, selectorSql);

        var (prefilter, logSourceChannels) = BuildPrefilters(rule);

        return new CompiledSigmaRule(
            rule,
            sourceFile ?? string.Empty,
            issues.Where(i => i.Severity == SigmaIssueSeverity.Warning).ToList(),
            conditionSql,
            composer.Parameters,
            prefilter ?? SigmaPrefilter.None,
            logSourceChannels,
            conditionEval);
    }

    public CompiledSigmaRule CompileYaml(string yaml, string? sourceFile = null) =>
        Compile(Parse(yaml), sourceFile);

    // -------------------- in-memory evaluation --------------------

    private SigmaSelectorEvaluator BuildSelectorEval(
        SigmaSelector selector,
        Dictionary<string, SigmaSelectorEvaluator> memo,
        IReadOnlyDictionary<string, SigmaSelector> all)
    {
        switch (selector)
        {
            case SigmaKeywordSelector keyword:
            {
                var matchers = keyword.Keywords.Select(BuildKeywordMatcher).ToArray();
                return (context, matched) =>
                {
                    var xml = context.Xml;
                    var hit = matchers.Any(matcher => matcher(xml));
                    if (hit)
                    {
                        matched.Add(keyword.Name);
                    }

                    return hit;
                };
            }

            case SigmaNameListSelector list:
            {
                var members = list.ReferencedSelectors
                    .Where(all.ContainsKey)
                    .Select(reference => SelectorEvalFor(reference, memo, all))
                    .ToArray();
                return (context, matched) =>
                {
                    var hit = members.Any(member => member(context, matched));
                    if (hit)
                    {
                        matched.Add(list.Name);
                    }

                    return hit;
                };
            }

            case SigmaFieldSelector fieldSelector:
            {
                var groups = fieldSelector.Groups
                    .Select(group => group.Fields.Select(BuildFieldEval).ToArray())
                    .ToArray();
                return (context, matched) =>
                {
                    var hit = groups.Any(group => group.All(field => field(context)));
                    if (hit)
                    {
                        matched.Add(fieldSelector.Name);
                    }

                    return hit;
                };
            }

            default:
                throw new InvalidOperationException($"Unsupported selector kind '{selector.GetType().Name}'.");
        }

        SigmaSelectorEvaluator SelectorEvalFor(
            string name,
            Dictionary<string, SigmaSelectorEvaluator> memoMap,
            IReadOnlyDictionary<string, SigmaSelector> allSelectors)
        {
            if (memoMap.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var built = BuildSelectorEval(allSelectors[name], memoMap, allSelectors);
            memoMap[name] = built;
            return built;
        }
    }

    /// <summary>Keyword matcher: case-insensitive containment, Sigma wildcards honored.</summary>
    private static Func<string, bool> BuildKeywordMatcher(string keyword)
    {
        if (SigmaPatterns.HasWildcard(keyword))
        {
            var pattern = SigmaPatterns.ToRegexPattern(keyword, SigmaMatchMode.Contains, ignoreCase: true);
            return text => SqlFunctions.MatchRegex(pattern, text);
        }

        return text => text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private SigmaConditionEvaluator BuildConditionEval(
        SigmaConditionNode node,
        Dictionary<string, SigmaSelectorEvaluator> selectorEvals)
    {
        switch (node)
        {
            case SigmaSelectorCondition condition:
            {
                var selectorEval = selectorEvals[condition.Name];
                return (context, matched) => selectorEval(context, matched);
            }
            case SigmaAndCondition and:
            {
                var terms = and.Terms.Select(t => BuildConditionEval(t, selectorEvals)).ToArray();
                return (context, matched) =>
                {
                    foreach (var term in terms)
                    {
                        if (!term(context, matched))
                        {
                            return false;
                        }
                    }

                    return true;
                };
            }
            case SigmaOrCondition orCondition:
            {
                var terms = orCondition.Terms.Select(t => BuildConditionEval(t, selectorEvals)).ToArray();
                return (context, matched) => terms.Any(term => term(context, matched));
            }
            case SigmaNotCondition notCondition:
            {
                var inner = BuildConditionEval(notCondition.Inner, selectorEvals);
                return (context, matched) => !inner(context, matched);
            }
            case SigmaOfCondition ofCondition:
            {
                var terms = ofCondition.Terms.Select(t => BuildConditionEval(t, selectorEvals)).ToArray();
                var required = ofCondition.IsAll ? terms.Length : ofCondition.Count;
                return (context, matched) =>
                {
                    var hits = 0;
                    foreach (var term in terms)
                    {
                        if (term(context, matched) && ++hits >= required)
                        {
                            return true;
                        }
                    }

                    return false;
                };
            }
            default:
                throw new InvalidOperationException($"Unsupported condition node '{node.GetType().Name}'.");
        }
    }

    /// <summary>Builds the in-memory predicate for one field match. Mirrors the SQL expression exactly.</summary>
    private Func<SigmaEvaluationContext, bool> BuildFieldEval(SigmaFieldMatch field)
    {
        var target = SigmaFieldRegistry.Resolve(field.Field);

        if (field.IsNullValue)
        {
            return target switch
            {
                SigmaFieldTarget.XmlData => context => context.XmlField(field.Field) is null,
                SigmaFieldTarget.Level => _ => false, // level / level_text are NOT NULL in the schema
                SigmaFieldTarget.EventId or SigmaFieldTarget.RecordId or SigmaFieldTarget.Keywords
                    or SigmaFieldTarget.ThreadId => context => GetLongField(context, target) is null,
                _ => context => GetStringField(context, target) is null,
            };
        }

        switch (target)
        {
            case SigmaFieldTarget.EventId or SigmaFieldTarget.RecordId or SigmaFieldTarget.Keywords
                or SigmaFieldTarget.ThreadId:
            {
                var values = field.Values.Select(ParseLong).ToArray();
                if (IsComparisonMode(field.Mode))
                {
                    var rhs = values[0];
                    return context =>
                    {
                        var actual = GetLongField(context, target);
                        return actual is not null && Compare(actual.Value, rhs, field.Mode);
                    };
                }

                return context =>
                {
                    var actual = GetLongField(context, target);
                    return actual is not null &&
                           (field.AllValues
                               ? values.All(value => actual.Value == value)
                               : values.Contains(actual.Value));
                };
            }

            case SigmaFieldTarget.Level:
            {
                var numericValues = field.Values.Where(IsNumeric).Select(ParseLong).ToArray();
                var textMatchers = field.Values.Where(v => !IsNumeric(v))
                    .Select(value => BuildStringMatcher(
                        value, field.Mode, ignoreCase: field.Mode == SigmaMatchMode.Regex
                            ? field.NoCase
                            : true))
                    .ToArray();
                return context =>
                {
                    var level = context.Event.Level;
                    var numericHit = numericValues.Length > 0 &&
                                     (IsComparisonMode(field.Mode)
                                         ? Compare(level, numericValues[0], field.Mode)
                                         : field.AllValues
                                             ? numericValues.All(value => level == (byte)value)
                                             : numericValues.Any(value => level == (byte)value));
                    var textHit = textMatchers.Length > 0 &&
                                  (field.AllValues
                                      ? textMatchers.All(matcher => matcher(context.Event.LevelText))
                                      : textMatchers.Any(matcher => matcher(context.Event.LevelText)));
                    return numericHit || textHit;
                };
            }

            case SigmaFieldTarget.XmlData:
            {
                var name = field.Field;
                var matchers = field.Values
                    .Select(value => BuildStringMatcher(value, field.Mode, field.NoCase))
                    .ToArray();
                return context =>
                {
                    var actual = context.XmlField(name);
                    return actual is not null &&
                           (field.AllValues
                               ? matchers.All(matcher => matcher(actual))
                               : matchers.Any(matcher => matcher(actual)));
                };
            }

            default:
            {
                var matchers = field.Values
                    .Select(value => BuildStringMatcher(value, field.Mode, ignoreCase: true))
                    .ToArray();
                return context =>
                {
                    var actual = GetStringField(context, target);
                    return actual is not null &&
                           (field.AllValues
                               ? matchers.All(matcher => matcher(actual))
                               : matchers.Any(matcher => matcher(actual)));
                };
            }
        }
    }

    /// <summary>
    /// Value matcher for text comparisons: Sigma wildcards and modifiers compile
    /// to a cached anchored regex; matching is case-insensitive by default
    /// (nocase only changes the case sensitivity of the 're' modifier).
    /// </summary>
    private static Func<string, bool> BuildStringMatcher(string value, SigmaMatchMode mode, bool ignoreCase)
    {
        var pattern = mode == SigmaMatchMode.Regex
            ? BuildRegexSource(value, ignoreCase)
            : SigmaPatterns.ToRegexPattern(value, mode, ignoreCase: true);
        return actual => SqlFunctions.MatchRegex(pattern, actual);
    }

    private static string BuildRegexSource(string value, bool noCase) =>
        noCase ? "(?i)" + value : value;

    // -------------------- SQL generation --------------------

    private static string SelectorSql(
        SigmaSelector selector,
        Dictionary<string, string> memo,
        IReadOnlyDictionary<string, SigmaSelector> all,
        SigmaSqlComposer composer)
    {
        if (memo.TryGetValue(selector.Name, out var existing))
        {
            return existing;
        }

        var sql = selector switch
        {
            SigmaKeywordSelector keyword => BuildKeywordSelectorSql(keyword, composer),
            SigmaNameListSelector list => BuildNameListSelectorSql(list, memo, all, composer),
            SigmaFieldSelector fieldSelector => BuildFieldSelectorSql(fieldSelector, composer),
            _ => throw new InvalidOperationException($"Unsupported selector kind '{selector.GetType().Name}'."),
        };

        memo[selector.Name] = sql;
        return sql;
    }

    private static string BuildKeywordSelectorSql(SigmaKeywordSelector keyword, SigmaSqlComposer composer)
    {
        var terms = keyword.Keywords.Select(keywordText =>
        {
            // Keywords match anywhere in the whole event (Sigma semantics);
            // the stored raw XML is the surrogate for the full log message.
            var parameter = composer.AddParameter(
                SigmaPatterns.ToLikePattern(keywordText, SigmaMatchMode.Contains));
            return SigmaSqlComposer.Coalesce($"e.xml LIKE {parameter} ESCAPE '\\'");
        }).ToList();

        return terms.Count == 1 ? terms[0] : "(" + string.Join(" OR ", terms) + ")";
    }

    private static string BuildNameListSelectorSql(
        SigmaNameListSelector list,
        Dictionary<string, string> memo,
        IReadOnlyDictionary<string, SigmaSelector> all,
        SigmaSqlComposer composer)
    {
        var terms = list.ReferencedSelectors
            .Where(all.ContainsKey)
            .Select(reference => SelectorSql(all[reference], memo, all, composer))
            .ToList();
        return terms.Count == 1 ? terms[0] : "(" + string.Join(" OR ", terms) + ")";
    }

    private static string BuildFieldSelectorSql(SigmaFieldSelector selector, SigmaSqlComposer composer)
    {
        var groupSql = selector.Groups
            .Select(group => group.Fields
                .Select(field => BuildFieldSql(field, composer))
                .ToList())
            .Select(fields => fields.Count == 1 ? fields[0] : "(" + string.Join(" AND ", fields) + ")")
            .ToList();

        return groupSql.Count == 1 ? groupSql[0] : "(" + string.Join(" OR ", groupSql) + ")";
    }

    private static string BuildFieldSql(SigmaFieldMatch field, SigmaSqlComposer composer)
    {
        var target = SigmaFieldRegistry.Resolve(field.Field);

        if (field.IsNullValue)
        {
            return target switch
            {
                SigmaFieldTarget.XmlData =>
                    $"xml_field(e.xml, {composer.AddParameter(field.Field)}) IS NULL",
                SigmaFieldTarget.Level => "(0 = 1)", // level is NOT NULL — a null check never matches
                _ => $"{ColumnFor(target)} IS NULL",
            };
        }

        if (target == SigmaFieldTarget.Level)
        {
            return BuildLevelFieldSql(field, composer);
        }

        if (target is SigmaFieldTarget.EventId or SigmaFieldTarget.RecordId
            or SigmaFieldTarget.Keywords or SigmaFieldTarget.ThreadId)
        {
            var column = ColumnFor(target);
            if (IsComparisonMode(field.Mode))
            {
                var parameter = composer.AddParameter(ParseLong(field.Values[0]));
                return SigmaSqlComposer.Coalesce($"{column} {SqlOperator(field.Mode)} {parameter}");
            }

            var valueParameters = field.Values.Select(value => composer.AddParameter(ParseLong(value))).ToList();
            var comparison = field.AllValues
                ? string.Join(" AND ", valueParameters.Select(p => $"{column} = {p}"))
                : valueParameters.Count == 1
                    ? $"{column} = {valueParameters[0]}"
                    : $"{column} IN ({string.Join(", ", valueParameters)})";
            return SigmaSqlComposer.Coalesce(comparison);
        }

        var baseExpression = target == SigmaFieldTarget.XmlData
            ? $"xml_field(e.xml, {composer.AddParameter(field.Field)})"
            : ColumnFor(target);

        var comparisons = field.Values
            .Select(value => BuildTextComparisonSql(baseExpression, value, field, composer))
            .ToList();

        return field.AllValues
            ? "(" + string.Join(" AND ", comparisons) + ")"
            : "(" + string.Join(" OR ", comparisons) + ")";
    }

    private static string BuildTextComparisonSql(
        string baseExpression, string value, SigmaFieldMatch field, SigmaSqlComposer composer)
    {
        return field.Mode switch
        {
            SigmaMatchMode.Regex => SigmaSqlComposer.Coalesce(
                $"{baseExpression} REGEXP {composer.AddParameter(BuildRegexSource(value, field.NoCase))}"),
            _ => SigmaSqlComposer.Coalesce(
                $"{baseExpression} LIKE {composer.AddParameter(SigmaPatterns.ToLikePattern(value, field.Mode))} ESCAPE '\\'"),
        };
    }

    /// <summary>
    /// Level accepts both Windows numeric levels (compared against the
    /// <c>level</c> column) and textual names (compared against
    /// <c>level_text</c>) — Sigma rules use both spellings.
    /// </summary>
    private static string BuildLevelFieldSql(SigmaFieldMatch field, SigmaSqlComposer composer)
    {
        var comparisons = new List<string>();

        foreach (var value in field.Values)
        {
            if (IsNumeric(value))
            {
                var parameter = composer.AddParameter(ParseLong(value));
                comparisons.Add(IsComparisonMode(field.Mode)
                    ? SigmaSqlComposer.Coalesce($"e.level {SqlOperator(field.Mode)} {parameter}")
                    : SigmaSqlComposer.Coalesce($"e.level = {parameter}"));
            }
            else
            {
                comparisons.Add(BuildTextComparisonSql("e.level_text", value, field, composer));
            }
        }

        return comparisons.Count == 1
            ? comparisons[0]
            : "(" + string.Join(field.AllValues ? " AND " : " OR ", comparisons) + ")";
    }

    private static string BuildConditionSql(
        SigmaConditionNode node,
        IReadOnlyDictionary<string, string> selectorSql)
    {
        switch (node)
        {
            case SigmaSelectorCondition condition:
                return $"({selectorSql[condition.Name]})";
            case SigmaAndCondition and:
                return "(" + string.Join(" AND ", and.Terms.Select(t => BuildConditionSql(t, selectorSql))) + ")";
            case SigmaOrCondition orCondition:
                return "(" + string.Join(" OR ", orCondition.Terms.Select(t => BuildConditionSql(t, selectorSql))) + ")";
            case SigmaNotCondition notCondition:
                return $"(NOT {BuildConditionSql(notCondition.Inner, selectorSql)})";
            case SigmaOfCondition ofCondition:
            {
                var terms = ofCondition.Terms.Select(t => $"({BuildConditionSql(t, selectorSql)})").ToList();
                var required = ofCondition.IsAll ? terms.Count : ofCondition.Count;
                return $"((({string.Join(" + ", terms)})) >= {required.ToString(CultureInfo.InvariantCulture)})";
            }
            default:
                throw new InvalidOperationException($"Unsupported condition node '{node.GetType().Name}'.");
        }
    }

    // -------------------- pre-filters --------------------

    /// <summary>
    /// Builds the sound pre-filter for a rule: channel/event-ID sets that a
    /// matching event MUST satisfy (soundness matters — an over-restrictive
    /// pre-filter would silently drop detections). Additionally returns the
    /// logsource-derived channel restriction, applied only when no selector
    /// declares a Channel field itself (explicit selector wins).
    /// </summary>
    private static (SigmaPrefilter? Prefilter, IReadOnlySet<string>? LogSourceChannels) BuildPrefilters(SigmaRule rule)
    {
        var prefilter = PrefilterOfCondition(rule.Detection.Condition, rule.Detection.Selectors);

        var selectorsDeclareChannel = rule.Detection.Selectors.Values
            .OfType<SigmaFieldSelector>()
            .SelectMany(s => s.Groups)
            .SelectMany(g => g.Fields)
            .Any(f => SigmaFieldRegistry.Resolve(f.Field) == SigmaFieldTarget.Channel && !f.IsNullValue);

        IReadOnlySet<string>? logSourceChannels = null;
        if (!selectorsDeclareChannel)
        {
            logSourceChannels = ChannelsForLogSource(rule.LogSource);
            prefilter = SigmaPrefilter.Intersect(prefilter, logSourceChannels is null
                ? null
                : new SigmaPrefilter(logSourceChannels, null));
        }

        return (prefilter, logSourceChannels);
    }

    private static SigmaPrefilter? PrefilterOfCondition(
        SigmaConditionNode node,
        IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        switch (node)
        {
            case SigmaSelectorCondition condition:
                return selectors.TryGetValue(condition.Name, out var selector)
                    ? PrefilterOfSelector(selector, selectors)
                    : null;
            case SigmaAndCondition and:
            {
                SigmaPrefilter? result = SigmaPrefilter.None;
                foreach (var term in and.Terms)
                {
                    result = SigmaPrefilter.Intersect(result, PrefilterOfCondition(term, selectors));
                    if (result is null)
                    {
                        return null;
                    }
                }

                return result;
            }
            case SigmaOrCondition orCondition:
            {
                SigmaPrefilter? result = null;
                foreach (var term in orCondition.Terms)
                {
                    result = SigmaPrefilter.Union(result, PrefilterOfCondition(term, selectors));
                    if (result is null)
                    {
                        return null;
                    }
                }

                return result;
            }
            case SigmaNotCondition:
                // NOT can match events outside any single selector's constraint space.
                return null;
            case SigmaOfCondition ofCondition:
            {
                var members = ofCondition.Terms
                    .Select(t => PrefilterOfCondition(t, selectors))
                    .ToList();
                if (members.Any(p => p is null))
                {
                    return null;
                }

                return ofCondition.IsAll
                    ? Fold(members, SigmaPrefilter.Intersect)
                    : Fold(members, SigmaPrefilter.Union);
            }
            default:
                return null;
        }
    }

    private static SigmaPrefilter? Fold(List<SigmaPrefilter?> members, Func<SigmaPrefilter?, SigmaPrefilter?, SigmaPrefilter?> combine)
    {
        var result = members[0];
        for (var index = 1; index < members.Count; index++)
        {
            result = combine(result, members[index]);
        }

        return result;
    }

    private static SigmaPrefilter? PrefilterOfSelector(
        SigmaSelector selector,
        IReadOnlyDictionary<string, SigmaSelector> selectors)
    {
        switch (selector)
        {
            case SigmaKeywordSelector:
                return null;

            case SigmaNameListSelector list:
            {
                SigmaPrefilter? result = null;
                foreach (var reference in list.ReferencedSelectors)
                {
                    if (!selectors.TryGetValue(reference, out var member))
                    {
                        return null;
                    }

                    result = SigmaPrefilter.Union(result, PrefilterOfSelector(member, selectors));
                    if (result is null)
                    {
                        return null;
                    }
                }

                return result;
            }

            case SigmaFieldSelector fieldSelector:
            {
                SigmaPrefilter? result = null;
                foreach (var group in fieldSelector.Groups)
                {
                    // Within a group fields are AND-linked; across groups OR-linked.
                    result = SigmaPrefilter.Union(result, PrefilterOfGroup(group));
                    if (result is null)
                    {
                        return null;
                    }
                }

                return result;
            }

            default:
                return null;
        }
    }

    private static SigmaPrefilter? PrefilterOfGroup(SigmaFieldGroup group)
    {
        List<string>? channels = null;
        List<long>? eventIds = null;
        var channelUnknown = false;
        var eventIdUnknown = false;

        foreach (var field in group.Fields)
        {
            if (field.IsNullValue || field.AllValues || field.Mode != SigmaMatchMode.Wildcard ||
                field.Values.Any(SigmaPatterns.HasWildcard))
            {
                switch (SigmaFieldRegistry.Resolve(field.Field))
                {
                    case SigmaFieldTarget.Channel: channelUnknown = true; break;
                    case SigmaFieldTarget.EventId: eventIdUnknown = true; break;
                }

                continue;
            }

            switch (SigmaFieldRegistry.Resolve(field.Field))
            {
                case SigmaFieldTarget.Channel when !channelUnknown:
                    channels = channels is null
                        ? field.Values.ToList()
                        : channels.Intersect(field.Values, StringComparer.Ordinal).ToList();
                    break;

                case SigmaFieldTarget.EventId when !eventIdUnknown && field.Values.All(v => TryParseLong(v, out _)):
                {
                    var values = field.Values.Select(ParseLong).ToList();
                    eventIds = eventIds is null ? values : eventIds.Intersect(values).ToList();
                    break;
                }

                case SigmaFieldTarget.EventId:
                    eventIdUnknown = true;
                    break;
            }
        }

        return new SigmaPrefilter(
            channelUnknown ? null : channels?.ToHashSet(StringComparer.OrdinalIgnoreCase),
            eventIdUnknown ? null : eventIds?.ToHashSet());
    }

    private static IReadOnlySet<string>? ChannelsForLogSource(SigmaLogSource? logSource)
    {
        if (logSource?.Product is not { } product ||
            !product.Equals("windows", StringComparison.OrdinalIgnoreCase) ||
            logSource.Service is not { } service)
        {
            return null;
        }

        return WindowsServiceChannels.TryGetValue(service, out var channel)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { channel }
            : null;
    }

    // -------------------- shared helpers --------------------

    private static string ColumnFor(SigmaFieldTarget target) => target switch
    {
        SigmaFieldTarget.Channel => "e.channel",
        SigmaFieldTarget.EventId => "e.event_id",
        SigmaFieldTarget.Provider => "e.provider",
        SigmaFieldTarget.Computer => "e.computer",
        SigmaFieldTarget.RecordId => "e.record_id",
        SigmaFieldTarget.Keywords => "e.keywords",
        SigmaFieldTarget.CorrelationId => "e.correlation_id",
        SigmaFieldTarget.ThreadId => "e.thread_id",
        SigmaFieldTarget.UserSid => "e.user_sid",
        _ => throw new InvalidOperationException($"No column for target '{target}'."),
    };

    private static string? GetStringField(SigmaEvaluationContext context, SigmaFieldTarget target) => target switch
    {
        SigmaFieldTarget.Channel => context.Event.Channel,
        SigmaFieldTarget.Provider => context.Event.Provider,
        SigmaFieldTarget.Computer => context.Event.Computer,
        SigmaFieldTarget.UserSid => context.Event.UserSid,
        SigmaFieldTarget.CorrelationId => context.Event.CorrelationId,
        _ => null,
    };

    private static long? GetLongField(SigmaEvaluationContext context, SigmaFieldTarget target) => target switch
    {
        SigmaFieldTarget.EventId => context.Event.EventId,
        SigmaFieldTarget.RecordId => context.Event.RecordId,
        SigmaFieldTarget.Keywords => context.Event.Keywords,
        SigmaFieldTarget.ThreadId => context.Event.ThreadId,
        _ => null,
    };

    private static string SqlOperator(SigmaMatchMode mode) => mode switch
    {
        SigmaMatchMode.GreaterThan => ">",
        SigmaMatchMode.GreaterOrEqual => ">=",
        SigmaMatchMode.LessThan => "<",
        SigmaMatchMode.LessOrEqual => "<=",
        _ => throw new InvalidOperationException($"Mode '{mode}' is not a comparison."),
    };

    private static bool IsComparisonMode(SigmaMatchMode mode) =>
        mode is SigmaMatchMode.GreaterThan or SigmaMatchMode.GreaterOrEqual
            or SigmaMatchMode.LessThan or SigmaMatchMode.LessOrEqual;

    private static bool Compare(long left, long right, SigmaMatchMode mode) => mode switch
    {
        SigmaMatchMode.GreaterThan => left > right,
        SigmaMatchMode.GreaterOrEqual => left >= right,
        SigmaMatchMode.LessThan => left < right,
        SigmaMatchMode.LessOrEqual => left <= right,
        _ => throw new InvalidOperationException($"Mode '{mode}' is not a comparison."),
    };

    private static bool IsNumeric(string value) => TryParseLong(value, out _);

    private static bool TryParseLong(string value, out long parsed)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return true;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(value[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
        {
            return true;
        }

        parsed = 0;
        return false;
    }

    private static long ParseLong(string value)
    {
        if (!TryParseLong(value, out var parsed))
        {
            // Validation rejects non-numeric values before compilation; this
            // guard keeps a programming error from becoming silent corruption.
            throw new InvalidOperationException($"Value '{value}' is not an integer.");
        }

        return parsed;
    }

    private static SigmaValidationIssue Err(string code, string message) =>
        new(SigmaIssueSeverity.Error, code, message);
}

/// <summary>
/// Sound pre-filter for a compiled rule: if set, a matching event must have its
/// channel in <see cref="Channels"/> and its event ID in <see cref="EventIds"/>.
/// A null set means "no constraint from this rule" — never a guessed value.
/// </summary>
public sealed record SigmaPrefilter(
    IReadOnlySet<string>? Channels,
    IReadOnlySet<long>? EventIds)
{
    public static SigmaPrefilter None { get; } = new(null, null);

    public bool CouldMatch(string channel, long eventId) =>
        (Channels is null || Channels.Any(candidate =>
            string.Equals(candidate, channel, StringComparison.OrdinalIgnoreCase))) &&
        (EventIds is null || EventIds.Contains(eventId));

    /// <summary>Intersects two constraints; either side unknown → unknown.</summary>
    public static SigmaPrefilter? Intersect(SigmaPrefilter? left, SigmaPrefilter? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        return new SigmaPrefilter(
            IntersectSets(left.Channels, right.Channels),
            IntersectSets(left.EventIds, right.EventIds));
    }

    /// <summary>Unions two constraints; either side unknown → unknown.</summary>
    public static SigmaPrefilter? Union(SigmaPrefilter? left, SigmaPrefilter? right)
    {
        if (left is null || right is null)
        {
            return null;
        }

        return new SigmaPrefilter(
            UnionSets(left.Channels, right.Channels),
            UnionSets(left.EventIds, right.EventIds));
    }

    private static IReadOnlySet<T>? IntersectSets<T>(IReadOnlySet<T>? left, IReadOnlySet<T>? right)
        where T : notnull
    {
        if (left is null || right is null)
        {
            return null;
        }

        return left.Intersect(right).ToHashSet();
    }

    private static IReadOnlySet<T>? UnionSets<T>(IReadOnlySet<T>? left, IReadOnlySet<T>? right)
        where T : notnull
    {
        if (left is null || right is null)
        {
            return null;
        }

        return left.Union(right).ToHashSet();
    }
}

/// <summary>Evaluates one selector; matched selector names accumulate for alert diagnostics.</summary>
public delegate bool SigmaSelectorEvaluator(SigmaEvaluationContext context, HashSet<string> matchedSelectors);

/// <summary>Evaluates the whole condition tree.</summary>
public delegate bool SigmaConditionEvaluator(SigmaEvaluationContext context, HashSet<string> matchedSelectors);

/// <summary>Per-event evaluation context with lazy, cached XML field extraction.</summary>
public sealed class SigmaEvaluationContext
{
    private readonly Dictionary<string, string?> _xmlFieldCache = new(StringComparer.Ordinal);

    public SigmaEvaluationContext(StoredEvent storedEvent) => Event = storedEvent;

    public StoredEvent Event { get; }

    public string Xml => Event.Xml;

    /// <summary>First <c>&lt;Data Name="…"&gt;</c> value from the raw event XML; null when absent.</summary>
    public string? XmlField(string name)
    {
        if (_xmlFieldCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var value = SqlFunctions.XmlField(Event.Xml, name);
        _xmlFieldCache[name] = value;
        return value;
    }
}

/// <summary>
/// A validated, executable Sigma rule: the parsed model, its parameterized SQL
/// WHERE clause (alias <c>e</c> = events table), parameter values, sound
/// pre-filters, and the in-memory condition evaluator.
/// </summary>
public sealed class CompiledSigmaRule
{
    internal CompiledSigmaRule(
        SigmaRule rule,
        string sourceFile,
        IReadOnlyList<SigmaValidationIssue> warnings,
        string whereClause,
        IReadOnlyList<(string Name, object? Value)> parameters,
        SigmaPrefilter prefilter,
        IReadOnlySet<string>? logSourceChannels,
        SigmaConditionEvaluator conditionEvaluator)
    {
        Rule = rule;
        SourceFile = sourceFile;
        Warnings = warnings;
        WhereClause = whereClause;
        Parameters = parameters;
        Prefilter = prefilter;
        LogSourceChannels = logSourceChannels;
        ConditionEvaluator = conditionEvaluator;
    }

    public SigmaRule Rule { get; }

    public string SourceFile { get; }

    public IReadOnlyList<SigmaValidationIssue> Warnings { get; }

    /// <summary>Parameterized boolean SQL expression over the events table (alias <c>e</c>).</summary>
    public string WhereClause { get; }

    public IReadOnlyList<(string Name, object? Value)> Parameters { get; }

    public SigmaPrefilter Prefilter { get; }

    /// <summary>Channel restriction from logsource (null when the rule declares channels itself, or logsource maps to none).</summary>
    public IReadOnlySet<string>? LogSourceChannels { get; }

    public SigmaConditionEvaluator ConditionEvaluator { get; }

    /// <summary>ATT&amp;CK technique IDs from the rule's <c>attack.t*</c> tags (e.g. T1059.001).</summary>
    public IReadOnlyList<string> TechniqueIds =>
        Rule.Tags.Select(AttackTechniqueStore.ParseTag)
            .Where(p => p.TechniqueId is not null)
            .Select(p => p.TechniqueId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>ATT&amp;CK tactic names from the rule's <c>attack.&lt;tactic&gt;</c> tags.</summary>
    public IReadOnlyList<string> TacticTags =>
        Rule.Tags.Select(AttackTechniqueStore.ParseTag)
            .Where(p => p.Tactic is not null)
            .Select(p => p.Tactic!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Evaluates the rule against one event (no pre-filter applied).</summary>
    public bool Evaluate(StoredEvent storedEvent, out HashSet<string> matchedSelectors)
    {
        matchedSelectors = new HashSet<string>(StringComparer.Ordinal);
        return ConditionEvaluator(new SigmaEvaluationContext(storedEvent), matchedSelectors);
    }

    /// <summary>Pre-filter fast path: false → the rule cannot match this event at all.</summary>
    public bool CouldMatch(StoredEvent storedEvent) =>
        Prefilter.CouldMatch(storedEvent.Channel, storedEvent.EventId) &&
        (LogSourceChannels is null || LogSourceChannels.Contains(storedEvent.Channel));
}

























