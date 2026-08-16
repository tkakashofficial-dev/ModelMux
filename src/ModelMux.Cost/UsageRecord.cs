namespace ModelMux.Cost;

/// <summary>
/// A single recorded LLM call. One record is written per <c>IChatClient</c> invocation,
/// streaming or otherwise.
/// </summary>
/// <remarks>
/// <see cref="Cost"/> is computed and stored at the time of the call. It is deliberately
/// not recomputed on read: provider prices change, and a historical record should reflect
/// what the call actually cost when it was made.
/// </remarks>
public sealed class UsageRecord
{
    /// <summary>Unique id for this record.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>When the call started, in UTC.</summary>
    public DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Model reported by the provider, falling back to the requested model.</summary>
    public string ModelId { get; init; } = "unknown";

    /// <summary>Provider name reported by the underlying client, when it exposes one.</summary>
    public string? ProviderName { get; init; }

    /// <summary>Prompt tokens billed for this call, including any cached portion.</summary>
    public long InputTokens { get; init; }

    /// <summary>Completion tokens billed for this call.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Total tokens as reported by the provider, or input plus output when it reports none.</summary>
    public long TotalTokens { get; init; }

    /// <summary>
    /// Input tokens served from the provider's prompt cache, when reported. These are billed
    /// at a reduced rate and are excluded from <see cref="InputTokens"/> for costing purposes.
    /// </summary>
    public long? CachedInputTokens { get; init; }

    /// <summary>Input tokens written to the provider's prompt cache, when reported.</summary>
    public long? CacheWriteTokens { get; init; }

    /// <summary>
    /// Computed cost in <see cref="Currency"/>, or <see langword="null"/> when no pricing entry
    /// matched <see cref="ModelId"/>. A null cost means "unknown", never "free".
    /// </summary>
    public decimal? Cost { get; init; }

    /// <summary>ISO 4217 currency code for <see cref="Cost"/>.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// True when the provider did not report token counts and they were estimated instead.
    /// Estimated records must never be presented as measured.
    /// </summary>
    public bool IsEstimated { get; init; }

    /// <summary>True when a pricing entry matched this model.</summary>
    public bool PricingFound { get; init; }

    /// <summary>Wall-clock duration of the call in milliseconds.</summary>
    public double DurationMs { get; init; }

    /// <summary>False when the call threw. Failed calls are still recorded â€” they cost latency, and often tokens.</summary>
    public bool Success { get; init; }

    /// <summary>Exception type name when <see cref="Success"/> is false.</summary>
    public string? ErrorType { get; init; }

    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True when the call was made through the streaming API.</summary>
    public bool Streamed { get; init; }

    // ---- Attribution -------------------------------------------------------

    /// <summary>Tenant this call is billed to, in a multi-tenant application.</summary>
    public string? TenantId { get; init; }

    /// <summary>Application-defined feature or route name, e.g. "invoice-extraction".</summary>
    public string? Feature { get; init; }

    /// <summary>End user who triggered the call, when the application tracks one.</summary>
    public string? UserId { get; init; }

    /// <summary>Provider-assigned response id, useful for correlating with provider dashboards.</summary>
    public string? ResponseId { get; init; }

    // ---- Optional content (opt-in; see CostTrackingOptions.RecordPromptContent) ----

    /// <summary>
    /// Prompt text. Only populated when content recording is explicitly enabled, because
    /// prompts routinely contain personal data.
    /// </summary>
    public string? Prompt { get; init; }

    /// <summary>Completion text. Only populated when content recording is explicitly enabled.</summary>
    public string? Completion { get; init; }
}
