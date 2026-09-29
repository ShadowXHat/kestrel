using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Kestrel.Core.Storage;

/// <summary>
/// SQLite user-defined functions registered on every event-store connection:
///   - <c>regexp(pattern, value)</c> — backs the <c>x REGEXP y</c> operator used by
///     the Sigma compiler's <c>re</c> value modifier.
///   - <c>xml_field(xml, name)</c> — extracts the text of the first
///     <c>&lt;Data Name="name"&gt;</c> element from a stored event's raw XML, giving
///     Sigma rules access to EventData fields (CommandLine, TargetImage, …) that
///     have no normalized column without formatting messages in the hot path
///     (the XML is already stored verbatim; nothing is parsed per-ingest).
///
/// Regexes are compiled once per pattern and cached process-wide — the scan
/// hot path must not pay Regex construction per row.
/// </summary>
public static class SqlFunctions
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, Regex> XmlFieldRegexCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Registers both functions on a freshly opened connection. Cheap: only
    /// creates delegates; compilation happens lazily on first use per pattern.
    /// </summary>
    public static void Register(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        // SQLite evaluates "x REGEXP y" as regexp(y, x) — (pattern, value).
        connection.CreateFunction<string?, string?, bool?>(
            "regexp",
            (pattern, value) => pattern is null || value is null
                ? false
                : MatchRegex(pattern, value),
            isDeterministic: true);

        connection.CreateFunction<string?, string?, string?>(
            "xml_field",
            (xml, name) => string.IsNullOrEmpty(xml) || string.IsNullOrEmpty(name)
                ? null
                : XmlField(xml, name),
            isDeterministic: true);
    }

    /// <summary>Regex match used by the regexp UDF and by in-memory rule evaluation.</summary>
    public static bool MatchRegex(string pattern, string value) => GetRegex(pattern).IsMatch(value);

    /// <summary>
    /// Text of the first <c>&lt;Data Name="…"&gt;…&lt;/Data&gt;</c> element with the
    /// requested name, or null when the element is absent. A present-but-empty
    /// element (including a self-closing one) yields the empty string — the
    /// distinction matters for Sigma <c>field: null</c> ("field is absent") checks.
    /// </summary>
    public static string? XmlField(string xml, string name)
    {
        var regex = XmlFieldRegexCache.GetOrAdd(name, static n => new Regex(
            $"""(?s)<Data\s+Name="{Regex.Escape(n)}"[^>]*>(?<value>.*?)</Data>""",
            RegexOptions.CultureInvariant));

        var match = regex.Match(xml);
        if (match.Success)
        {
            return match.Groups["value"].Value;
        }

        // Self-closing element: present, but no text content.
        var emptyRegex = XmlFieldRegexCache.GetOrAdd(
            "\0self-closing:" + name,
            static n => new Regex(
                $"""<Data\s+Name="{Regex.Escape(n["\0self-closing:".Length..])}"[^>]*/>""",
                RegexOptions.CultureInvariant));

        return emptyRegex.IsMatch(xml) ? string.Empty : null;
    }

    private static Regex GetRegex(string pattern) => RegexCache.GetOrAdd(pattern, static p =>
        new Regex(p, RegexOptions.Compiled | RegexOptions.CultureInvariant));
}
