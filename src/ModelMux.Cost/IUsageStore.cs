namespace ModelMux.Cost;

/// <summary>
/// Persistence for <see cref="UsageRecord"/>. Implement this to send usage somewhere
/// other than the built-in stores (a warehouse, a queue, an existing metrics pipeline).
/// </summary>
/// <remarks>
/// Implementations must not throw. A telemetry failure should never fail the caller's
/// LLM request; log and drop instead.
/// </remarks>
public interface IUsageStore
{
    /// <summary>Persists a single usage record.</summary>
    ValueTask AddAsync(UsageRecord record, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read access to recorded usage. Kept separate from <see cref="IUsageStore"/> so a
/// write-only sink (queue, event stream) can implement one without the other.
/// </summary>
public interface IUsageQuery
{
    /// <summary>Returns records matching the filter, newest first.</summary>
    ValueTask<IReadOnlyList<UsageRecord>> QueryAsync(
        UsageFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>Returns aggregate totals for the records matching the filter.</summary>
    ValueTask<UsageSummary> SummarizeAsync(
        UsageFilter filter,
        CancellationToken cancellationToken = default);
}

/// <summary>Filter for <see cref="IUsageQuery"/>. All properties are optional and combine with AND.</summary>
public sealed class UsageFilter
{
    /// <summary>Inclusive lower bound on <see cref="UsageRecord.TimestampUtc"/>.</summary>
    public DateTimeOffset? FromUtc { get; init; }

    /// <summary>Inclusive upper bound on <see cref="UsageRecord.TimestampUtc"/>.</summary>
    public DateTimeOffset? ToUtc { get; init; }

    /// <summary>Restrict to a single tenant.</summary>
    public string? TenantId { get; init; }

    /// <summary>Restrict to a single feature.</summary>
    public string? Feature { get; init; }

    /// <summary>Restrict to a single model id.</summary>
    public string? ModelId { get; init; }

    /// <summary>Restrict to successful or failed calls.</summary>
    public bool? Success { get; init; }

    /// <summary>Maximum records to return. Ignored by <see cref="IUsageQuery.SummarizeAsync"/>.</summary>
    public int Limit { get; init; } = 100;
}

/// <summary>Aggregate totals over a set of usage records.</summary>
public sealed class UsageSummary
{
    /// <summary>Number of calls recorded.</summary>
    public long RequestCount { get; init; }

    /// <summary>Number of calls that completed without an exception.</summary>
    public long SuccessCount { get; init; }

    /// <summary>Number of calls that threw.</summary>
    public long FailureCount { get; init; }

    /// <summary>Sum of input tokens.</summary>
    public long InputTokens { get; init; }

    /// <summary>Sum of output tokens.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Sum of total tokens.</summary>
    public long TotalTokens { get; init; }

    /// <summary>Total cost of records that had a known price.</summary>
    public decimal Cost { get; init; }

    /// <summary>ISO 4217 currency code for <see cref="Cost"/>.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>
    /// Number of records with no matching pricing entry. When this is non-zero,
    /// <see cref="Cost"/> understates real spend and should be labelled as a lower bound.
    /// </summary>
    public long UnpricedCount { get; init; }

    /// <summary>Number of records whose token counts were estimated rather than reported.</summary>
    public long EstimatedCount { get; init; }

    /// <summary>Mean wall-clock duration across the matched records, in milliseconds.</summary>
    public double AverageDurationMs { get; init; }
}
