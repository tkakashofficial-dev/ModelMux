using Microsoft.Extensions.Options;
using ModelMux.Cost.Pricing;

namespace ModelMux.Cost.Tests;

public class PricingResolverTests
{
    [Fact]
    public void Resolves_exact_model_id()
    {
        var resolver = new PricingResolver(BuiltInPricing.All);

        var price = resolver.Resolve("claude-opus-5");

        Assert.NotNull(price);
        Assert.Equal(5.00m, price.InputPerMillion);
        Assert.Equal(25.00m, price.OutputPerMillion);
    }

    [Fact]
    public void Resolves_case_insensitively()
    {
        var resolver = new PricingResolver(BuiltInPricing.All);

        Assert.NotNull(resolver.Resolve("CLAUDE-OPUS-5"));
    }

    [Fact]
    public void Falls_back_to_prefix_so_date_suffixed_ids_still_price()
    {
        var resolver = new PricingResolver(BuiltInPricing.All);

        var price = resolver.Resolve("claude-haiku-4-5-20251001");

        Assert.NotNull(price);
        Assert.Equal(1.00m, price.InputPerMillion);
    }

    [Fact]
    public void Prefers_the_longest_matching_prefix()
    {
        var resolver = new PricingResolver(
        [
            new ModelPrice { Model = "gpt", InputPerMillion = 1m, OutputPerMillion = 1m },
            new ModelPrice { Model = "gpt-4o-mini", InputPerMillion = 9m, OutputPerMillion = 9m },
        ]);

        var price = resolver.Resolve("gpt-4o-mini-2024-07-18");

        Assert.NotNull(price);
        Assert.Equal(9m, price.InputPerMillion);
    }

    [Fact]
    public void Returns_null_for_unknown_model()
    {
        var resolver = new PricingResolver(BuiltInPricing.All);

        Assert.Null(resolver.Resolve("some-model-nobody-priced"));
    }

    [Fact]
    public void Returns_null_for_null_or_blank_model()
    {
        var resolver = new PricingResolver(BuiltInPricing.All);

        Assert.Null(resolver.Resolve(null));
        Assert.Null(resolver.Resolve("   "));
    }

    [Fact]
    public void User_pricing_overrides_a_built_in_entry()
    {
        var options = Options.Create(new CostTrackingOptions
        {
            Pricing =
            [
                new ModelPrice { Model = "claude-opus-5", InputPerMillion = 99m, OutputPerMillion = 199m },
            ],
        });

        var price = new PricingResolver(options).Resolve("claude-opus-5");

        Assert.NotNull(price);
        Assert.Equal(99m, price.InputPerMillion);
    }

    [Fact]
    public void Built_in_pricing_can_be_switched_off_entirely()
    {
        var options = Options.Create(new CostTrackingOptions { UseBuiltInPricing = false });

        Assert.Null(new PricingResolver(options).Resolve("claude-opus-5"));
    }

    [Fact]
    public void Every_built_in_price_carries_a_verification_date_and_source()
    {
        // A cost tool whose prices silently rot is worse than one with no prices,
        // so provenance is a hard requirement on every shipped entry.
        Assert.NotEmpty(BuiltInPricing.All);

        foreach (var price in BuiltInPricing.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(price.Model));
            Assert.True(price.InputPerMillion > 0, $"{price.Model} has no input price");
            Assert.True(price.OutputPerMillion > 0, $"{price.Model} has no output price");
            Assert.NotNull(price.LastVerified);
            Assert.False(string.IsNullOrWhiteSpace(price.Source));
        }
    }
}

public class CostCalculatorTests
{
    private static CostCalculator Calculator(params ModelPrice[] prices) =>
        new(new PricingResolver(prices.Length == 0 ? BuiltInPricing.All : prices));

    [Fact]
    public void Computes_cost_from_per_million_rates()
    {
        // 1,000,000 in @ $5 + 1,000,000 out @ $25 = $30.
        var result = Calculator().Calculate("claude-opus-5", 1_000_000, 1_000_000);

        Assert.True(result.PriceFound);
        Assert.Equal(30.00m, result.Cost);
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public void Scales_correctly_for_small_token_counts()
    {
        // 1,000 in @ $5/M = $0.005; 500 out @ $25/M = $0.0125. Total $0.0175.
        var result = Calculator().Calculate("claude-opus-5", 1_000, 500);

        Assert.Equal(0.0175m, result.Cost);
    }

    [Fact]
    public void Returns_unknown_rather_than_zero_for_an_unpriced_model()
    {
        // Reporting an unpriced call as $0 would silently understate spend.
        var result = Calculator().Calculate("mystery-model", 1_000, 1_000);

        Assert.False(result.PriceFound);
        Assert.Null(result.Cost);
        Assert.Null(result.Currency);
    }

    [Fact]
    public void Bills_cached_input_at_the_cached_rate()
    {
        var price = new ModelPrice
        {
            Model = "m",
            InputPerMillion = 10m,
            OutputPerMillion = 0m,
            CachedInputPerMillion = 1m,
        };

        // 1M input of which 800k cached: 200k @ $10/M + 800k @ $1/M = $2 + $0.8 = $2.80.
        var result = Calculator(price).Calculate("m", 1_000_000, 0, cachedInputTokens: 800_000);

        Assert.Equal(2.80m, result.Cost);
    }

    [Fact]
    public void Bills_cache_writes_at_the_write_rate()
    {
        var price = new ModelPrice
        {
            Model = "m",
            InputPerMillion = 10m,
            OutputPerMillion = 0m,
            CacheWritePerMillion = 12.5m,
        };

        // 1M input of which 400k written: 600k @ $10/M + 400k @ $12.5/M = $6 + $5 = $11.
        var result = Calculator(price).Calculate("m", 1_000_000, 0, cacheWriteTokens: 400_000);

        Assert.Equal(11.00m, result.Cost);
    }

    [Fact]
    public void Falls_back_to_the_input_rate_when_no_cache_rate_is_configured()
    {
        var price = new ModelPrice { Model = "m", InputPerMillion = 10m, OutputPerMillion = 0m };

        var result = Calculator(price).Calculate("m", 1_000_000, 0, cachedInputTokens: 500_000);

        Assert.Equal(10.00m, result.Cost);
    }

    [Fact]
    public void Treats_negative_token_counts_as_zero()
    {
        var result = Calculator().Calculate("claude-opus-5", -5, -5);

        Assert.Equal(0m, result.Cost);
    }
}
