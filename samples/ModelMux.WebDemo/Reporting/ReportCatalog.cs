namespace ModelMux.WebDemo.Reporting;

/// <summary>Type of a report field, which determines how a filter value is parsed and compared.</summary>
public enum FieldType
{
    /// <summary>Text.</summary>
    Text,

    /// <summary>Whole number.</summary>
    Number,

    /// <summary>Money amount.</summary>
    Money,

    /// <summary>Calendar date.</summary>
    Date,
}

/// <summary>A field a report may be filtered on.</summary>
/// <param name="Name">Field name as it appears in an intent.</param>
/// <param name="Type">How values are parsed and compared.</param>
/// <param name="Description">Sent to the model so it knows what the field means.</param>
public sealed record ReportField(string Name, FieldType Type, string Description);

/// <summary>A report the model is allowed to target.</summary>
/// <param name="Name">Report name as it appears in an intent.</param>
/// <param name="Description">Sent to the model.</param>
/// <param name="Fields">The only fields that may be filtered on.</param>
public sealed record ReportDefinition(string Name, string Description, IReadOnlyList<ReportField> Fields);

/// <summary>
/// The allowlist of reports and fields the model may reference.
/// </summary>
/// <remarks>
/// Anything not described here is rejected by the validator, no matter how confidently the
/// model asks for it. This is what makes a hallucinated field a 400 rather than an incident.
/// </remarks>
public static class ReportCatalog
{
    /// <summary>The only report in this demo.</summary>
    public static ReportDefinition EmployeeSummary { get; } = new(
        "EmployeeSummary",
        "One row per employee, with department, joining date, leave taken, salary and status.",
        [
            new ReportField("Name", FieldType.Text, "Employee's full name."),
            new ReportField("Department", FieldType.Text, "Department name, e.g. Engineering, Sales, Finance."),
            new ReportField("JoiningDate", FieldType.Date, "Date the employee joined, ISO-8601 (yyyy-MM-dd)."),
            new ReportField("LeaveDays", FieldType.Number, "Leave days taken this year."),
            new ReportField("Salary", FieldType.Money, "Annual salary."),
            new ReportField("Status", FieldType.Text, "Either Active or Inactive."),
        ]);

    /// <summary>Every available report, keyed case-insensitively.</summary>
    public static IReadOnlyDictionary<string, ReportDefinition> All { get; } =
        new Dictionary<string, ReportDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [EmployeeSummary.Name] = EmployeeSummary,
        };

    /// <summary>
    /// Renders the catalog for the model's prompt. Describing the vocabulary is what keeps
    /// generated intents inside it most of the time; the validator handles the rest.
    /// </summary>
    public static string Describe()
    {
        var lines = new List<string>();

        foreach (var report in All.Values)
        {
            lines.Add($"Report \"{report.Name}\": {report.Description}");
            lines.Add("Fields:");

            foreach (var field in report.Fields)
            {
                lines.Add($"  - {field.Name} ({field.Type}): {field.Description}");
            }
        }

        lines.Add($"Operators: {string.Join(", ", FilterOperator.All)}");

        return string.Join("\n", lines);
    }
}
