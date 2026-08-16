using System.Collections.Concurrent;

namespace ModelMux.Cost.Stores;

/// <summary>
/// In-memory usage store with a bounded ring of recent records.
/// </summary>
/// <remarks>
/// Intended for development, tests, and single-process demos. Records are lost on restart
/// and are not shared across instances — use a persistent store for anything you plan to
/// bill or report against.
/// </remarks>
public sealed class InMemoryUsageStore : IUsageStore, IUsageQuery
{
    private readonly ConcurrentQueue<UsageRecord> _records = new();
    private readonly int _capacity;

    /// <param name="capacity">
    /// Maximum records retained. Oldest are evicted first so a long-running process
    /// doesn't grow without bound.
    /// </param>
    public InMemoryUsageStore(int capacity = 10_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <inheritdoc />
    public ValueTask AddAsync(UsageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        _records.Enqueue(record);

        while (_records.Count > _capacity && _records.TryDequeue(out _))
        {
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<UsageRecord>> QueryAsync(
        UsageFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        IReadOnlyList<UsageRecord> results =
        [
            .. Match(filter)
                .OrderByDescending(r => r.TimestampUtc)
                .Take(Math.Max(0, filter.Limit))
        ];

        return ValueTask.FromResult(results);
    }

    /// <inheritdoc />
    public ValueTask<UsageSummary> SummarizeAsync(
        UsageFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var matched = Match(filter).ToList();

        var summary = new UsageSummary
        {
            RequestCount = matched.Count,
            SuccessCount = matched.Count(r => r.Success),
            FailureCount = matched.Count(r => !r.Success),
            InputTokens = matched.Sum(r => r.InputTokens),
            OutputTokens = matched.Sum(r => r.OutputTokens),
            TotalTokens = matched.Sum(r => r.TotalTokens),
            Cost = matched.Sum(r => r.Cost ?? 0m),
            Currency = matched.FirstOrDefault(r => r.Currency is not null)?.Currency ?? "USD",
            UnpricedCount = matched.Count(r => !r.PricingFound),
            EstimatedCount = matched.Count(r => r.IsEstimated),
            AverageDurationMs = matched.Count == 0 ? 0 : matched.Average(r => r.DurationMs),
        };

        return ValueTask.FromResult(summary);
    }

    private IEnumerable<UsageRecord> Match(UsageFilter filter)
    {
        IEnumerable<UsageRecord> query = _records;

        if (filter.FromUtc is { } from)
        {
            query = query.Where(r => r.TimestampUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(r => r.TimestampUtc <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.TenantId))
        {
            query = query.Where(r => string.Equals(r.TenantId, filter.TenantId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.Feature))
        {
            query = query.Where(r => string.Equals(r.Feature, filter.Feature, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(filter.ModelId))
        {
            query = query.Where(r => string.Equals(r.ModelId, filter.ModelId, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Success is { } success)
        {
            query = query.Where(r => r.Success == success);
        }

        return query;
    }
}
