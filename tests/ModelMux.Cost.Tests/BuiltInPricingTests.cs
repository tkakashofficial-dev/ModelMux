using ModelMux.Cost.Pricing;

namespace ModelMux.Cost.Tests;

/// <summary>
/// Guards the shipped price catalogue. The dangerous failure here is silent: a model that
/// resolves to a similarly-named entry reports a plausible but wrong cost, and nobody
/// notices until an invoice disagrees.
/// </summary>
public class BuiltInPricingTests
{
    private static readonly PricingResolver Resolver = new(BuiltInPricing.All);

    [Fact]
    public void Catalogue_has_no_duplicate_model_ids()
    {
        var duplicates = BuiltInPricing.All
            .GroupBy(p => p.Model, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_entry_has_provenance_and_a_positive_price()
    {
        foreach (var price in BuiltInPricing.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(price.Model));
            Assert.True(price.InputPerMillion > 0, $"{price.Model}: input price must be positive");
            Assert.True(price.OutputPerMillion > 0, $"{price.Model}: output price must be positive");
            Assert.NotNull(price.LastVerified);
            Assert.False(string.IsNullOrWhiteSpace(price.Source));
            Assert.Equal("USD", price.Currency);
        }
    }

    [Fact]
    public void Output_is_never_cheaper_than_input()
    {
        // True of every provider's published pricing. A violation means a transcription slip.
        foreach (var price in BuiltInPricing.All)
        {
            Assert.True(
                price.OutputPerMillion >= price.InputPerMillion,
                $"{price.Model}: output ({price.OutputPerMillion}) < input ({price.InputPerMillion})");
        }
    }

    [Fact]
    public void Cached_input_is_never_more_expensive_than_normal_input()
    {
        foreach (var price in BuiltInPricing.All.Where(p => p.CachedInputPerMillion is not null))
        {
            Assert.True(
                price.CachedInputPerMillion <= price.InputPerMillion,
                $"{price.Model}: cached input costs more than uncached");
        }
    }

    // The prefix fallback is what lets date-suffixed ids resolve. Its risk is that a shorter
    // name swallows a longer one — these pairs are the ones that would actually collide.
    [Theory]
    [InlineData("gpt-5-mini", 0.25)]
    [InlineData("gpt-5-nano", 0.05)]
    [InlineData("gpt-5-pro", 15.00)]
    [InlineData("gpt-5", 1.25)]
    [InlineData("gpt-4o-mini", 0.15)]
    [InlineData("gpt-4o", 2.50)]
    [InlineData("gpt-4.1-mini", 0.40)]
    [InlineData("gpt-4.1-nano", 0.10)]
    [InlineData("gpt-4.1", 2.00)]
    [InlineData("gpt-5.4-mini", 0.75)]
    [InlineData("gpt-5.4-pro", 30.00)]
    [InlineData("gpt-5.4", 2.50)]
    [InlineData("o1-pro", 150.00)]
    [InlineData("o1", 15.00)]
    [InlineData("o3-mini", 1.10)]
    [InlineData("o3-pro", 20.00)]
    [InlineData("o3", 2.00)]
    [InlineData("gemini-2.5-flash-lite", 0.10)]
    [InlineData("gemini-2.5-flash", 0.30)]
    [InlineData("gemini-2.5-pro", 1.25)]
    [InlineData("gemini-3.5-flash-lite", 0.30)]
    [InlineData("gemini-3.5-flash", 1.50)]
    [InlineData("claude-opus-5", 5.00)]
    [InlineData("claude-sonnet-5", 3.00)]
    [InlineData("claude-haiku-4-5", 1.00)]
    public void Similar_model_names_resolve_to_their_own_price(string modelId, double expectedInput)
    {
        var price = Resolver.Resolve(modelId);

        Assert.NotNull(price);
        Assert.Equal(modelId, price.Model, ignoreCase: true);
        Assert.Equal((decimal)expectedInput, price.InputPerMillion);
    }

    [Theory]
    [InlineData("gpt-4o-2024-08-06", "gpt-4o")]
    [InlineData("gpt-5-mini-2026-01-15", "gpt-5-mini")]
    [InlineData("claude-opus-5-20260101", "claude-opus-5")]
    [InlineData("gemini-2.5-flash-002", "gemini-2.5-flash")]
    public void Dated_variants_fall_back_to_their_base_model(string requested, string expectedBase)
    {
        var price = Resolver.Resolve(requested);

        Assert.NotNull(price);
        Assert.Equal(expectedBase, price.Model, ignoreCase: true);
    }

    [Fact]
    public void All_three_providers_are_represented()
    {
        Assert.NotEmpty(BuiltInPricing.Anthropic);
        Assert.NotEmpty(BuiltInPricing.OpenAI);
        Assert.NotEmpty(BuiltInPricing.Google);

        Assert.Equal(
            BuiltInPricing.Anthropic.Count + BuiltInPricing.OpenAI.Count + BuiltInPricing.Google.Count,
            BuiltInPricing.All.Count);
    }
}
