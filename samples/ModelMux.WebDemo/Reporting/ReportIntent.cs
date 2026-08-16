using System.Text.Json.Serialization;

namespace ModelMux.WebDemo.Reporting;

/// <summary>
/// A structured, validated description of what the user asked for.
/// </summary>
/// <remarks>
/// <para>
/// This type is the security boundary of the reporting demo. The model produces an *intent*,
/// never SQL and never a LINQ expression. The application then validates every field and
/// operator against an allowlist before anything executes.
/// </para>
/// <para>
/// The reason is simple: a model that can emit arbitrary SQL is a SQL-injection vector with a
/// natural-language front end. Constraining it to a fixed vocabulary means the worst a bad or
/// adversarial generation can do is produce an intent the validator rejects.
/// </para>
/// </remarks>
public sealed class ReportIntent
{
    /// <summary>Report the user wants. Validated against <see cref="ReportCatalog"/>.</summary>
    [JsonPropertyName("report")]
    public string Report { get; set; } = string.Empty;

    /// <summary>Filters to apply. Every one is validated before execution.</summary>
    [JsonPropertyName("filters")]
    public List<ReportFilter> Filters { get; set; } = [];
}

/// <summary>One condition in a <see cref="ReportIntent"/>.</summary>
public sealed class ReportFilter
{
    /// <summary>Field to filter on. Must appear in the report's allowlist.</summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    /// <summary>Comparison to apply. Must be a member of <see cref="FilterOperator"/>.</summary>
    [JsonPropertyName("operator")]
    public string Operator { get; set; } = string.Empty;

    /// <summary>Value to compare against, as text. Parsed according to the field's type.</summary>
    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>The complete set of comparisons a generated intent may use.</summary>
public static class FilterOperator
{
    /// <summary>Exact equality. Named <c>EqualTo</c> in code to avoid hiding <c>object.Equals</c>.</summary>
    public const string EqualTo = "Equals";

    /// <summary>Strictly greater than.</summary>
    public const string GreaterThan = "GreaterThan";

    /// <summary>Greater than or equal to.</summary>
    public const string GreaterThanOrEqual = "GreaterThanOrEqual";

    /// <summary>Strictly less than.</summary>
    public const string LessThan = "LessThan";

    /// <summary>Less than or equal to.</summary>
    public const string LessThanOrEqual = "LessThanOrEqual";

    /// <summary>Case-insensitive substring match.</summary>
    public const string Contains = "Contains";

    /// <summary>Every operator the validator will accept.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EqualTo, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Contains,
    };
}

/// <summary>Outcome of validating a generated intent.</summary>
/// <param name="IsValid">Whether the intent is safe to execute.</param>
/// <param name="Errors">Why it was rejected, when it was.</param>
public readonly record struct IntentValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>A passing result.</summary>
    public static IntentValidationResult Valid() => new(true, []);

    /// <summary>A failing result carrying the reasons.</summary>
    public static IntentValidationResult Invalid(IReadOnlyList<string> errors) => new(false, errors);
}
