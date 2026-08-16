using System.Globalization;

namespace ModelMux.WebDemo.Reporting;

/// <summary>A synthetic employee record. Entirely fabricated demo data.</summary>
/// <param name="Name">Employee name.</param>
/// <param name="Department">Department.</param>
/// <param name="JoiningDate">Date joined.</param>
/// <param name="LeaveDays">Leave days taken this year.</param>
/// <param name="Salary">Annual salary.</param>
/// <param name="Status">Active or Inactive.</param>
public sealed record Employee(
    string Name,
    string Department,
    DateOnly JoiningDate,
    int LeaveDays,
    decimal Salary,
    string Status);

/// <summary>
/// In-memory synthetic data, and the only component that executes a validated intent.
/// </summary>
/// <remarks>
/// Filters are applied with LINQ predicates chosen by a <c>switch</c> over the validated
/// operator. No expression is ever built from model output, so there is no code path in which
/// generated text becomes executable.
/// </remarks>
public sealed class EmployeeRepository
{
    private static readonly Employee[] Data =
    [
        new("Asha Menon", "Engineering", new DateOnly(2026, 2, 11), 14, 1_850_000m, "Active"),
        new("Ravi Kulkarni", "Engineering", new DateOnly(2023, 7, 3), 6, 2_400_000m, "Active"),
        new("Meera Nair", "Finance", new DateOnly(2026, 1, 20), 12, 1_600_000m, "Active"),
        new("Tomas Weber", "Sales", new DateOnly(2024, 11, 8), 21, 1_450_000m, "Active"),
        new("Priya Sharma", "Engineering", new DateOnly(2026, 4, 1), 3, 1_200_000m, "Active"),
        new("Daniel Osei", "Sales", new DateOnly(2022, 5, 16), 18, 1_700_000m, "Inactive"),
        new("Lin Zhao", "Finance", new DateOnly(2025, 9, 30), 9, 1_950_000m, "Active"),
        new("Hannah Bergstrom", "People", new DateOnly(2026, 3, 12), 11, 1_100_000m, "Active"),
        new("Omar Haddad", "Engineering", new DateOnly(2021, 8, 23), 25, 2_800_000m, "Inactive"),
        new("Grace Ofori", "People", new DateOnly(2024, 2, 5), 4, 1_050_000m, "Active"),
    ];

    /// <summary>Every synthetic employee.</summary>
    public IReadOnlyList<Employee> All => Data;

    /// <summary>
    /// Applies an intent that has already been validated.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The intent was not validated first. Executing an unvalidated intent is the one thing
    /// this design exists to prevent, so it fails loudly rather than quietly doing its best.
    /// </exception>
    public IReadOnlyList<Employee> Execute(ReportIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        if (!IntentValidator.Validate(intent).IsValid)
        {
            throw new InvalidOperationException(
                "Refusing to execute an intent that failed validation.");
        }

        IEnumerable<Employee> query = Data;

        foreach (var filter in intent.Filters)
        {
            query = Apply(query, filter);
        }

        return [.. query];
    }

    private static IEnumerable<Employee> Apply(IEnumerable<Employee> query, ReportFilter filter)
    {
        // Field and operator are both known-good by this point, so this switch is exhaustive
        // over the allowlist rather than over arbitrary strings.
        return filter.Field.ToLowerInvariant() switch
        {
            "name" => TextFilter(query, e => e.Name, filter),
            "department" => TextFilter(query, e => e.Department, filter),
            "status" => TextFilter(query, e => e.Status, filter),
            "leavedays" => NumberFilter(query, e => e.LeaveDays, int.Parse(filter.Value, CultureInfo.InvariantCulture), filter.Operator),
            "salary" => NumberFilter(query, e => e.Salary, decimal.Parse(filter.Value, CultureInfo.InvariantCulture), filter.Operator),
            "joiningdate" => NumberFilter(query, e => e.JoiningDate, DateOnly.Parse(filter.Value, CultureInfo.InvariantCulture), filter.Operator),
            _ => query,
        };
    }

    private static IEnumerable<Employee> TextFilter(
        IEnumerable<Employee> query,
        Func<Employee, string> selector,
        ReportFilter filter) =>
        filter.Operator.ToLowerInvariant() switch
        {
            "equals" => query.Where(e => string.Equals(selector(e), filter.Value, StringComparison.OrdinalIgnoreCase)),
            "contains" => query.Where(e => selector(e).Contains(filter.Value, StringComparison.OrdinalIgnoreCase)),
            _ => query,
        };

    private static IEnumerable<Employee> NumberFilter<T>(
        IEnumerable<Employee> query,
        Func<Employee, T> selector,
        T value,
        string op)
        where T : IComparable<T> =>
        op.ToLowerInvariant() switch
        {
            "equals" => query.Where(e => selector(e).CompareTo(value) == 0),
            "greaterthan" => query.Where(e => selector(e).CompareTo(value) > 0),
            "greaterthanorequal" => query.Where(e => selector(e).CompareTo(value) >= 0),
            "lessthan" => query.Where(e => selector(e).CompareTo(value) < 0),
            "lessthanorequal" => query.Where(e => selector(e).CompareTo(value) <= 0),
            _ => query,
        };
}
