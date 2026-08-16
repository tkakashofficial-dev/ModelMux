namespace ModelMux.Cost.Pricing;

/// <summary>Resolves a model id to its price.</summary>
public interface IPricingResolver
{
    /// <summary>
    /// Returns the price for <paramref name="modelId"/>, or <see langword="null"/> when no
    /// entry matches. Callers must treat null as "unknown cost", not "free".
    /// </summary>
    ModelPrice? Resolve(string? modelId);
}

/// <summary>Computes cost from token counts and a resolved price.</summary>
public interface ICostCalculator
{
    /// <summary>
    /// Computes the cost of a call. Cached and cache-write tokens are treated as a subset
    /// of <paramref name="inputTokens"/> and billed at their own rates.
    /// </summary>
    CostResult Calculate(
        string? modelId,
        long inputTokens,
        long outputTokens,
        long? cachedInputTokens = null,
        long? cacheWriteTokens = null);
}

/// <summary>Outcome of a cost calculation.</summary>
/// <param name="Cost">The computed cost, or null when no price was found.</param>
/// <param name="Currency">Currency of <paramref name="Cost"/>, or null when no price was found.</param>
/// <param name="PriceFound">Whether a pricing entry matched the model.</param>
public readonly record struct CostResult(decimal? Cost, string? Currency, bool PriceFound)
{
    /// <summary>Result used when no pricing entry matched the model.</summary>
    public static CostResult Unknown => new(null, null, false);
}
