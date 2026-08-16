namespace ModelMux.Cost.Pricing;

/// <summary>
/// Price for one model, expressed per million tokens â€” the unit every major provider
/// publishes, so entries can be copied from a pricing page without conversion.
/// </summary>
public sealed class ModelPrice
{
    /// <summary>
    /// Model id this entry applies to. Matching is case-insensitive, exact first, then
    /// longest-prefix, so <c>claude-opus-5</c> also matches <c>claude-opus-5-20260101</c>.
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Price per one million prompt tokens.</summary>
    public decimal InputPerMillion { get; set; }

    /// <summary>Price per one million completion tokens.</summary>
    public decimal OutputPerMillion { get; set; }

    /// <summary>
    /// Price for input tokens served from the provider's prompt cache. When null, cached
    /// tokens are billed at <see cref="InputPerMillion"/>.
    /// </summary>
    public decimal? CachedInputPerMillion { get; set; }

    /// <summary>
    /// Price for input tokens written to the provider's prompt cache. When null, cache
    /// writes are billed at <see cref="InputPerMillion"/>.
    /// </summary>
    public decimal? CacheWritePerMillion { get; set; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Date this price was last checked against the provider's published pricing.
    /// Prices go stale; surface this in any UI that reports cost.
    /// </summary>
    public DateOnly? LastVerified { get; set; }

    /// <summary>Where the price came from, e.g. a pricing page URL.</summary>
    public string? Source { get; set; }
}
