using Microsoft.Extensions.Options;

namespace ModelMux.Cost.Pricing;

/// <summary>
/// Default <see cref="IPricingResolver"/>. Matches exact model id first, then falls back to
/// the longest matching prefix so date-suffixed ids (<c>claude-opus-5-20260101</c>) resolve
/// against their base entry.
/// </summary>
public sealed class PricingResolver : IPricingResolver
{
    private readonly Dictionary<string, ModelPrice> _exact;
    private readonly List<ModelPrice> _byPrefixLengthDesc;

    /// <summary>Builds a resolver from configured options, including built-in prices when enabled.</summary>
    public PricingResolver(IOptions<CostTrackingOptions> options)
        : this(BuildCatalog(options?.Value ?? throw new ArgumentNullException(nameof(options))))
    {
    }

    /// <summary>Builds a resolver from an explicit set of prices. Later entries win on duplicate model ids.</summary>
    public PricingResolver(IEnumerable<ModelPrice> prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        _exact = new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase);
        foreach (var price in prices)
        {
            if (!string.IsNullOrWhiteSpace(price.Model))
            {
                // Later entries win, which is what lets user config override built-ins.
                _exact[price.Model] = price;
            }
        }

        _byPrefixLengthDesc = [.. _exact.Values.OrderByDescending(p => p.Model.Length)];
    }

    /// <inheritdoc />
    public ModelPrice? Resolve(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        if (_exact.TryGetValue(modelId, out var exact))
        {
            return exact;
        }

        foreach (var candidate in _byPrefixLengthDesc)
        {
            if (modelId.StartsWith(candidate.Model, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Built-in entries first, user entries second, so a user entry with the same model id
    /// replaces the built-in one.
    /// </summary>
    private static IEnumerable<ModelPrice> BuildCatalog(CostTrackingOptions options)
    {
        if (options.UseBuiltInPricing)
        {
            foreach (var price in BuiltInPricing.All)
            {
                yield return price;
            }
        }

        foreach (var price in options.Pricing)
        {
            yield return price;
        }
    }
}
