using ModelMux.Cost.Pricing;

namespace ModelMux.Cost;

/// <summary>Configuration for ModelMux.Cost.</summary>
public sealed class CostTrackingOptions
{
    /// <summary>Configuration section name used by the <c>AddCostTracking(IConfiguration)</c> overload.</summary>
    public const string SectionName = "ModelMux.Cost";

    /// <summary>
    /// When false, the middleware passes calls straight through and records nothing.
    /// Lets you disable tracking per-environment without changing the DI graph.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Record prompt and completion text alongside token counts. <b>Off by default:</b>
    /// prompts routinely contain personal data, and turning this on makes your usage
    /// store a copy of it. Enable deliberately, and only where you have a basis to.
    /// </summary>
    public bool RecordPromptContent { get; set; }

    /// <summary>
    /// Maximum characters kept per prompt/completion when <see cref="RecordPromptContent"/>
    /// is enabled. Longer text is truncated.
    /// </summary>
    public int MaxRecordedContentLength { get; set; } = 2_000;

    /// <summary>
    /// Estimate token counts when the provider reports none (common with local models
    /// such as Ollama). Estimated records are flagged via <see cref="UsageRecord.IsEstimated"/>
    /// and must never be presented as measured.
    /// </summary>
    public bool EstimateTokensWhenMissing { get; set; } = true;

    /// <summary>Include the pricing entries that ship with ModelMux.Cost. User entries always win.</summary>
    public bool UseBuiltInPricing { get; set; } = true;

    /// <summary>
    /// Pricing entries for models ModelMux.Cost doesn't ship prices for, or overrides for the
    /// ones it does. Matched by <see cref="ModelPrice.Model"/>.
    /// </summary>
    public IList<ModelPrice> Pricing { get; set; } = [];

    /// <summary>
    /// Tenant recorded when no ambient tenant is set. Useful for single-tenant apps that
    /// still want the column populated.
    /// </summary>
    public string? DefaultTenantId { get; set; }

    /// <summary>Feature name recorded when no ambient feature is set.</summary>
    public string? DefaultFeature { get; set; }
}
