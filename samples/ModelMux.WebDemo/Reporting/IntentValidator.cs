using System.Globalization;

namespace ModelMux.WebDemo.Reporting;

/// <summary>
/// Checks a generated <see cref="ReportIntent"/> against the catalog before anything executes.
/// </summary>
/// <remarks>
/// Treat every intent as untrusted input, because that is exactly what it is: text produced by
/// a model, possibly influenced by whatever the user typed. Validation is not a formality here;
/// it is the control that makes the feature safe.
/// </remarks>
public static class IntentValidator
{
    /// <summary>Validates the intent, returning every problem rather than just the first.</summary>
    public static IntentValidationResult Validate(ReportIntent? intent)
    {
        var errors = new List<string>();

        if (intent is null)
        {
            return IntentValidationResult.Invalid(["The model did not return an intent."]);
        }

        if (!ReportCatalog.All.TryGetValue(intent.Report, out var report))
        {
            errors.Add(
                $"Unknown report '{intent.Report}'. Available: {string.Join(", ", ReportCatalog.All.Keys)}.");

            // Without a report there is nothing to validate fields against.
            return IntentValidationResult.Invalid(errors);
        }

        var allowedFields = report.Fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

        // A cap on filters: an intent with hundreds of conditions is either a bad generation
        // or an attempt to make the query expensive.
        if (intent.Filters.Count > 10)
        {
            errors.Add($"Too many filters ({intent.Filters.Count}). At most 10 are allowed.");
        }

        foreach (var filter in intent.Filters)
        {
            if (!allowedFields.TryGetValue(filter.Field, out var field))
            {
                errors.Add(
                    $"Unknown field '{filter.Field}'. Available on {report.Name}: "
                    + $"{string.Join(", ", allowedFields.Keys)}.");
                continue;
            }

            if (!FilterOperator.All.Contains(filter.Operator))
            {
                errors.Add(
                    $"Unknown operator '{filter.Operator}' on field '{filter.Field}'. "
                    + $"Available: {string.Join(", ", FilterOperator.All)}.");
                continue;
            }

            if (!IsValueParseable(field.Type, filter.Value))
            {
                errors.Add(
                    $"Value '{filter.Value}' is not a valid {field.Type} for field '{field.Name}'.");
                continue;
            }

            if (string.Equals(filter.Operator, FilterOperator.Contains, StringComparison.OrdinalIgnoreCase)
                && field.Type != FieldType.Text)
            {
                errors.Add($"Operator 'Contains' only applies to text fields, not '{field.Name}'.");
            }
        }

        return errors.Count == 0 ? IntentValidationResult.Valid() : IntentValidationResult.Invalid(errors);
    }

    private static bool IsValueParseable(FieldType type, string value) => type switch
    {
        FieldType.Text => !string.IsNullOrWhiteSpace(value),
        FieldType.Number => int.TryParse(value, CultureInfo.InvariantCulture, out _),
        FieldType.Money => decimal.TryParse(value, CultureInfo.InvariantCulture, out _),
        FieldType.Date => DateOnly.TryParse(value, CultureInfo.InvariantCulture, out _),
        _ => false,
    };
}
