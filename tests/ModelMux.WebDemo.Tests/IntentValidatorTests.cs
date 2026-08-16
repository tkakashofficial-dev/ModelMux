using ModelMux.WebDemo.Reporting;

namespace ModelMux.WebDemo.Tests;

/// <summary>
/// The validator is the security boundary between model output and query execution. Treat
/// every case here as adversarial: the model may hallucinate, and a user may be trying to
/// steer it somewhere it shouldn't go.
/// </summary>
public class IntentValidatorTests
{
    private static ReportIntent Intent(params ReportFilter[] filters) => new()
    {
        Report = "EmployeeSummary",
        Filters = [.. filters],
    };

    private static ReportFilter Filter(string field, string op, string value) =>
        new() { Field = field, Operator = op, Value = value };

    [Fact]
    public void A_well_formed_intent_passes()
    {
        var result = IntentValidator.Validate(Intent(
            Filter("Status", "Equals", "Active"),
            Filter("LeaveDays", "GreaterThan", "10"),
            Filter("JoiningDate", "GreaterThanOrEqual", "2026-01-01")));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void A_null_intent_is_rejected()
    {
        // The model can return nothing at all; that must not become an empty query that
        // silently returns every row.
        var result = IntentValidator.Validate(null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void An_unknown_report_is_rejected_and_the_real_ones_are_listed()
    {
        var intent = new ReportIntent { Report = "SalaryLeaks", Filters = [] };

        var result = IntentValidator.Validate(intent);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SalaryLeaks", StringComparison.Ordinal));
        Assert.Contains(result.Errors, e => e.Contains("EmployeeSummary", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hallucinated_field_is_rejected()
    {
        // Models invent plausible-sounding fields. This is the most likely failure in practice.
        var result = IntentValidator.Validate(Intent(
            Filter("SocialSecurityNumber", "Equals", "123")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("SocialSecurityNumber", StringComparison.Ordinal));
    }

    [Fact]
    public void An_unknown_operator_is_rejected()
    {
        var result = IntentValidator.Validate(Intent(
            Filter("Status", "DropTable", "Active")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("DropTable", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("LeaveDays", "GreaterThan", "not-a-number")]
    [InlineData("JoiningDate", "GreaterThan", "last tuesday")]
    [InlineData("Salary", "LessThan", "a lot")]
    public void A_value_that_does_not_parse_for_its_field_type_is_rejected(
        string field,
        string op,
        string value)
    {
        var result = IntentValidator.Validate(Intent(Filter(field, op, value)));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Contains_is_rejected_on_non_text_fields()
    {
        var result = IntentValidator.Validate(Intent(Filter("Salary", "Contains", "5")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Contains", StringComparison.Ordinal));
    }

    [Fact]
    public void An_absurd_number_of_filters_is_rejected()
    {
        // Guards against a runaway generation making the query needlessly expensive.
        var many = Enumerable.Range(0, 50)
            .Select(_ => Filter("Status", "Equals", "Active"))
            .ToArray();

        var result = IntentValidator.Validate(Intent(many));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Every_problem_is_reported_not_just_the_first()
    {
        // One round-trip should tell you everything wrong, not make you play whack-a-mole.
        var result = IntentValidator.Validate(Intent(
            Filter("Nonexistent", "Equals", "x"),
            Filter("Status", "Nonsense", "Active"),
            Filter("LeaveDays", "GreaterThan", "abc")));

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void Field_and_operator_names_are_case_insensitive()
    {
        // Models are inconsistent about casing; that alone shouldn't fail a valid request.
        var result = IntentValidator.Validate(Intent(Filter("status", "equals", "Active")));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void An_empty_filter_list_is_valid_and_means_no_filtering()
    {
        Assert.True(IntentValidator.Validate(Intent()).IsValid);
    }
}

public class EmployeeRepositoryTests
{
    private readonly EmployeeRepository _repository = new();

    private static ReportIntent Intent(params ReportFilter[] filters) => new()
    {
        Report = "EmployeeSummary",
        Filters = [.. filters],
    };

    private static ReportFilter Filter(string field, string op, string value) =>
        new() { Field = field, Operator = op, Value = value };

    [Fact]
    public void An_unvalidated_intent_is_refused_rather_than_executed()
    {
        // Defence in depth: even if a caller forgets to validate, execution won't proceed.
        var malicious = Intent(Filter("SecretField", "Equals", "x"));

        Assert.Throws<InvalidOperationException>(() => _repository.Execute(malicious));
    }

    [Fact]
    public void No_filters_returns_every_row()
    {
        Assert.Equal(_repository.All.Count, _repository.Execute(Intent()).Count);
    }

    [Fact]
    public void Filters_combine_with_AND()
    {
        var rows = _repository.Execute(Intent(
            Filter("Status", "Equals", "Active"),
            Filter("Department", "Equals", "Engineering")));

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("Active", r.Status));
        Assert.All(rows, r => Assert.Equal("Engineering", r.Department));
    }

    [Fact]
    public void The_worked_example_from_the_spec_returns_the_right_rows()
    {
        // "Active employees who joined this year and took more than 10 leave days."
        var rows = _repository.Execute(Intent(
            Filter("Status", "Equals", "Active"),
            Filter("JoiningDate", "GreaterThanOrEqual", "2026-01-01"),
            Filter("LeaveDays", "GreaterThan", "10")));

        Assert.All(rows, r =>
        {
            Assert.Equal("Active", r.Status);
            Assert.True(r.JoiningDate >= new DateOnly(2026, 1, 1));
            Assert.True(r.LeaveDays > 10);
        });

        Assert.Contains(rows, r => r.Name == "Asha Menon");
        Assert.DoesNotContain(rows, r => r.Name == "Ravi Kulkarni");
    }

    [Fact]
    public void Numeric_comparisons_work_on_money_fields()
    {
        var rows = _repository.Execute(Intent(Filter("Salary", "GreaterThan", "2000000")));

        Assert.All(rows, r => Assert.True(r.Salary > 2_000_000m));
    }

    [Fact]
    public void Contains_matches_substrings_case_insensitively()
    {
        var rows = _repository.Execute(Intent(Filter("Name", "Contains", "menon")));

        Assert.Single(rows);
        Assert.Equal("Asha Menon", rows[0].Name);
    }
}
