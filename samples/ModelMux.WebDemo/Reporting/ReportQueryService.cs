using ModelMux.WebDemo.Reporting;

namespace ModelMux.WebDemo.Reporting;

/// <summary>Result of turning a natural-language question into report rows.</summary>
/// <param name="Question">What the user asked.</param>
/// <param name="Intent">The structured intent the model produced, for transparency.</param>
/// <param name="Rows">Matching rows, when the intent validated.</param>
/// <param name="ValidationErrors">Why the intent was rejected, when it was.</param>
public sealed record ReportQueryResult(
    string Question,
    ReportIntent? Intent,
    IReadOnlyList<Employee> Rows,
    IReadOnlyList<string> ValidationErrors)
{
    /// <summary>Whether the question produced executable results.</summary>
    public bool Succeeded => ValidationErrors.Count == 0 && Intent is not null;
}

/// <summary>
/// Natural language in, validated report rows out.
/// </summary>
/// <remarks>
/// <para>The pipeline is deliberately four separate steps:</para>
/// <list type="number">
///   <item><description>The model turns the question into a structured intent.</description></item>
///   <item><description>The application validates that intent against an allowlist.</description></item>
///   <item><description>Only then does the application execute it.</description></item>
///   <item><description>The intent is returned to the caller so the decision is inspectable.</description></item>
/// </list>
/// <para>
/// The model never writes SQL and never chooses what executes. It only proposes; the
/// application disposes.
/// </para>
/// <para>
/// Note that this class names no AI provider. Which model interprets the question is a
/// configuration decision.
/// </para>
/// </remarks>
public sealed class ReportQueryService(IModelMux mux, EmployeeRepository repository, ILogger<ReportQueryService> logger)
{
    private const string ProfileName = "smart";

    /// <summary>Answers a natural-language question about the employee report.</summary>
    public async Task<ReportQueryResult> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var prompt =
            $"""
             Convert the user's question into a report intent.

             Available reports and fields:
             {ReportCatalog.Describe()}

             Rules:
             - Use only the report, fields and operators listed above. Never invent one.
             - Dates must be ISO-8601 (yyyy-MM-dd). Today is {DateOnly.FromDateTime(DateTime.UtcNow):yyyy-MM-dd}.
             - "This year" means on or after {DateTime.UtcNow.Year}-01-01.
             - Numbers and money must be plain digits with no separators or currency symbols.
             - If the question implies no filter, return an empty filters array.

             User question: {question}
             """;

        // Structured output constrains the model to the intent's schema. Validation still
        // runs afterwards, because a schema-valid intent can still name a field that
        // doesn't exist.
        var intent = await mux.GetStructuredResponseAsync<ReportIntent>(
            prompt, ProfileName, cancellationToken);

        var validation = IntentValidator.Validate(intent);

        if (!validation.IsValid)
        {
            logger.LogWarning(
                "Rejected a generated report intent for question {Question}: {Errors}",
                question,
                string.Join("; ", validation.Errors));

            return new ReportQueryResult(question, intent, [], validation.Errors);
        }

        return new ReportQueryResult(question, intent, repository.Execute(intent!), []);
    }
}
