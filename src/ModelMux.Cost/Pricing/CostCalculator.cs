namespace ModelMux.Cost.Pricing;

/// <summary>Default <see cref="ICostCalculator"/>: token counts times per-million rates.</summary>
public sealed class CostCalculator : ICostCalculator
{
    private const decimal PerMillion = 1_000_000m;

    private readonly IPricingResolver _resolver;

    /// <summary>Creates a calculator backed by the given pricing resolver.</summary>
    public CostCalculator(IPricingResolver resolver)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    /// <inheritdoc />
    public CostResult Calculate(
        string? modelId,
        long inputTokens,
        long outputTokens,
        long? cachedInputTokens = null,
        long? cacheWriteTokens = null)
    {
        var price = _resolver.Resolve(modelId);
        if (price is null)
        {
            return CostResult.Unknown;
        }

        // Providers report cached and cache-write tokens as a subset of the input count.
        // Bill each portion at its own rate and the remainder at the standard input rate.
        var cached = Math.Max(0, cachedInputTokens ?? 0);
        var written = Math.Max(0, cacheWriteTokens ?? 0);
        var uncached = Math.Max(0, inputTokens - cached - written);

        var cost =
            (uncached * price.InputPerMillion
             + cached * (price.CachedInputPerMillion ?? price.InputPerMillion)
             + written * (price.CacheWritePerMillion ?? price.InputPerMillion)
             + Math.Max(0, outputTokens) * price.OutputPerMillion)
            / PerMillion;

        return new CostResult(cost, price.Currency, true);
    }
}
