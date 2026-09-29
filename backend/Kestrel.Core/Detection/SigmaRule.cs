namespace Kestrel.Core.Detection;

/// <summary>Sigma rule status (Sigma v1.0 spec).</summary>
public enum SigmaStatus
{
    Undefined,
    Stable,
    Test,
    Experimental,
    Deprecated,
    Unsupported,
}

/// <summary>Sigma rule severity level.</summary>
public enum SigmaSeverity
{
    Undefined,
    Informational,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// How a field value is compared. <see cref="SigmaMatchMode.Wildcard"/> is the
/// Sigma default: case-insensitive full match where <c>*</c> matches any
/// sequence and <c>?</c> a single character.
/// </summary>
public enum SigmaMatchMode
{
    Wildcard,
    Contains,
    StartsWith,
    EndsWith,
    Regex,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
}

/// <summary>Sigma <c>logsource</c> block.</summary>
public sealed record SigmaLogSource(
    string? Category,
    string? Product,
    string? Service,
    string? Definition);

/// <summary>
/// One field match inside a field-map selector: the Sigma field name, its
/// value(s), the parsed value modifiers and the comparison mode. A list of
/// values matches ANY value unless <see cref="AllValues"/> is set (the
/// <c>all</c> modifier), in which case every value must match.
/// </summary>
public sealed record SigmaFieldMatch(
    string Field,
    bool IsNullValue,
    IReadOnlyList<string> Values,
    bool AllValues,
    SigmaMatchMode Mode,
    bool NoCase);

/// <summary>One AND-linked map of field matches. A selector with several groups matches if ANY group matches (Sigma list-of-maps).</summary>
public sealed record SigmaFieldGroup(IReadOnlyList<SigmaFieldMatch> Fields);

/// <summary>Field-map selector: <c>field|modifier: value</c> entries. Fields within a group are AND-linked; groups are OR-linked.</summary>
public sealed record SigmaFieldSelector(
    string Name,
    IReadOnlyList<SigmaFieldGroup> Groups) : SigmaSelector(Name);

/// <summary>A named detection selector. Kind depends on the YAML value shape.</summary>
public abstract record SigmaSelector(string Name);

/// <summary>Keyword-list selector: bare list of strings matched against the whole event.</summary>
public sealed record SigmaKeywordSelector(
    string Name,
    IReadOnlyList<string> Keywords) : SigmaSelector(Name);

/// <summary>Name-list selector: a list referencing other selectors (implicit OR).</summary>
public sealed record SigmaNameListSelector(
    string Name,
    IReadOnlyList<string> ReferencedSelectors) : SigmaSelector(Name);

/// <summary>Condition expression AST (Sigma v1.0 boolean logic).</summary>
public abstract record SigmaConditionNode;

public sealed record SigmaSelectorCondition(string Name) : SigmaConditionNode;

public sealed record SigmaAndCondition(IReadOnlyList<SigmaConditionNode> Terms) : SigmaConditionNode;

public sealed record SigmaOrCondition(IReadOnlyList<SigmaConditionNode> Terms) : SigmaConditionNode;

public sealed record SigmaNotCondition(SigmaConditionNode Inner) : SigmaConditionNode;

/// <summary>
/// <c>N of …</c> / <c>all of …</c> quantifier over the expanded selector terms.
/// Matches when at least <see cref="Count"/> of the terms match
/// (<see cref="IsAll"/> expands to the term count).
/// </summary>
public sealed record SigmaOfCondition(
    int Count,
    bool IsAll,
    IReadOnlyList<SigmaConditionNode> Terms) : SigmaConditionNode;

/// <summary>The <c>detection</c> block: named selectors plus the condition.</summary>
public sealed record SigmaDetection(
    IReadOnlyDictionary<string, SigmaSelector> Selectors,
    SigmaConditionNode Condition,
    string? Timeframe);

/// <summary>A parsed Sigma rule (v1.0 spec fields).</summary>
public sealed record SigmaRule(
    string Title,
    string? Id,
    SigmaStatus Status,
    string? Description,
    string? Author,
    string? License,
    IReadOnlyList<string> References,
    string? Date,
    SigmaLogSource? LogSource,
    SigmaDetection Detection,
    SigmaSeverity Level,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> FalsePositives);

public enum SigmaIssueSeverity
{
    Error,
    Warning,
}

/// <summary>One validation diagnostic (blocking errors vs non-blocking warnings).</summary>
public sealed record SigmaValidationIssue(
    SigmaIssueSeverity Severity,
    string Code,
    string Message);

/// <summary>Thrown for structural YAML failures (malformed YAML, wrong shapes).</summary>
public sealed class SigmaParseException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>Thrown when validation produced blocking errors; carries the diagnostics.</summary>
public sealed class SigmaCompileException(IReadOnlyList<SigmaValidationIssue> errors)
    : Exception($"Sigma rule failed validation with {errors.Count} error(s): " +
                string.Join(" | ", errors.Select(e => $"{e.Code}: {e.Message}")))
{
    public IReadOnlyList<SigmaValidationIssue> Errors { get; } = errors;
}
