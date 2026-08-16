namespace ModelMux.Cost.Pricing;

/// <summary>
/// Pricing entries that ship with ModelMux.Cost.
/// </summary>
/// <remarks>
/// <para>
/// Every entry here was read from the provider's own published pricing page and carries a
/// <see cref="ModelPrice.LastVerified"/> date and <see cref="ModelPrice.Source"/> URL.
/// Nothing is estimated: a guessed price in a cost tool is worse than no price, because it
/// is reported with the same confidence as a correct one and nobody re-checks it.
/// </para>
/// <para>
/// Models with no entry are recorded with a null cost and surface as
/// <see cref="UsageSummary.UnpricedCount"/> â€” never as zero.
/// </para>
/// <para>
/// <b>Prices go stale.</b> Re-check against <see cref="ModelPrice.Source"/> periodically, and
/// override anything out of date via <c>CostTrackingOptions.Pricing</c> or configuration without
/// waiting for a package update.
/// </para>
/// <para>
/// <b>Known limitation:</b> some providers price by prompt size (Gemini Pro models charge more
/// above a 200k-token prompt) or apply time-limited promotional rates.
/// <see cref="ModelPrice"/> expresses one flat rate per model, so those entries use the
/// standard tier and record the caveat in their <see cref="ModelPrice.Source"/>. Cost for very
/// large prompts on those models is therefore a lower bound.
/// </para>
/// </remarks>
public static class BuiltInPricing
{
    private static readonly DateOnly AnthropicVerified = new(2026, 6, 24);
    private static readonly DateOnly OpenAiVerified = new(2026, 8, 16);
    private static readonly DateOnly GoogleVerified = new(2026, 8, 16);

    private const string AnthropicSource = "https://platform.claude.com/docs/en/pricing";
    private const string OpenAiSource = "https://developers.openai.com/api/docs/pricing";
    private const string GoogleSource = "https://ai.google.dev/gemini-api/docs/pricing";

    /// <summary>Anthropic first-party API rates, in USD per million tokens.</summary>
    /// <remarks>
    /// Cached-input and cache-write rates are derived from Anthropic's documented multipliers
    /// against the base input price: cache reads bill at ~0.1x and 5-minute writes at 1.25x.
    /// </remarks>
    public static IReadOnlyList<ModelPrice> Anthropic { get; } =
    [
        Claude("claude-fable-5", 10.00m, 50.00m),
        Claude("claude-mythos-5", 10.00m, 50.00m),
        Claude("claude-opus-5", 5.00m, 25.00m),
        Claude("claude-opus-4-8", 5.00m, 25.00m),
        Claude("claude-opus-4-7", 5.00m, 25.00m),
        Claude("claude-opus-4-6", 5.00m, 25.00m),
        Claude("claude-sonnet-5", 3.00m, 15.00m),
        Claude("claude-sonnet-4-6", 3.00m, 15.00m),
        Claude("claude-haiku-4-5", 1.00m, 5.00m),
    ];

    /// <summary>OpenAI API rates, in USD per million tokens.</summary>
    public static IReadOnlyList<ModelPrice> OpenAI { get; } =
    [
        OpenAi("gpt-5.6-sol", 5.00m, 30.00m, 0.50m),
        OpenAi("gpt-5.6-terra", 2.00m, 12.00m, 0.20m),
        OpenAi("gpt-5.6-luna", 0.20m, 1.20m, 0.02m),
        OpenAi("gpt-5.6-cyber", 12.50m, 75.00m, 1.25m),
        OpenAi("gpt-5.5-cyber", 12.50m, 75.00m, 1.25m),
        OpenAi("gpt-5.5-pro", 30.00m, 180.00m),
        OpenAi("gpt-5.5", 5.00m, 30.00m, 0.50m),
        OpenAi("gpt-5.4-pro", 30.00m, 180.00m),
        OpenAi("gpt-5.4-mini", 0.75m, 4.50m, 0.075m),
        OpenAi("gpt-5.4-nano", 0.20m, 1.25m, 0.02m),
        OpenAi("gpt-5.4", 2.50m, 15.00m, 0.25m),
        OpenAi("gpt-5.3-codex", 1.75m, 14.00m, 0.175m),
        OpenAi("gpt-5.2-pro", 21.00m, 168.00m),
        OpenAi("gpt-5.2", 1.75m, 14.00m, 0.175m),
        OpenAi("gpt-5.1", 1.25m, 10.00m, 0.125m),
        OpenAi("gpt-5-pro", 15.00m, 120.00m),
        OpenAi("gpt-5-mini", 0.25m, 2.00m, 0.025m),
        OpenAi("gpt-5-nano", 0.05m, 0.40m, 0.005m),
        OpenAi("gpt-5-search-api", 1.25m, 10.00m, 0.125m),
        OpenAi("gpt-5", 1.25m, 10.00m, 0.125m),
        OpenAi("gpt-4.1-mini", 0.40m, 1.60m, 0.10m),
        OpenAi("gpt-4.1-nano", 0.10m, 0.40m, 0.025m),
        OpenAi("gpt-4.1", 2.00m, 8.00m, 0.50m),
        OpenAi("gpt-4o-mini", 0.15m, 0.60m, 0.075m),
        OpenAi("gpt-4o-2024-05-13", 5.00m, 15.00m),
        OpenAi("gpt-4o", 2.50m, 10.00m, 1.25m),
        OpenAi("o1-pro", 150.00m, 600.00m),
        OpenAi("o1", 15.00m, 60.00m, 7.50m),
        OpenAi("o3-pro", 20.00m, 80.00m),
        OpenAi("o3-mini", 1.10m, 4.40m, 0.55m),
        OpenAi("o3", 2.00m, 8.00m, 0.50m),
        OpenAi("o4-mini", 1.10m, 4.40m, 0.275m),
        OpenAi("chat-latest", 5.00m, 30.00m, 0.50m),
        OpenAi("gpt-4-turbo-2024-04-09", 10.00m, 30.00m),
        OpenAi("gpt-4-0613", 30.00m, 60.00m),
        OpenAi("gpt-3.5-turbo-instruct", 1.50m, 2.00m),
        OpenAi("gpt-3.5-turbo-1106", 1.00m, 2.00m),
        OpenAi("gpt-3.5-turbo-0125", 0.50m, 1.50m),
        OpenAi("gpt-3.5-turbo", 0.50m, 1.50m),
    ];

    /// <summary>Google Gemini API rates (paid tier), in USD per million tokens.</summary>
    public static IReadOnlyList<ModelPrice> Google { get; } =
    [
        Gemini("gemini-3.7-flash", 0.75m, 3.75m, 0.075m, "promotional rate through 2026-12-31"),
        Gemini("gemini-3.6-flash", 0.75m, 3.75m, 0.075m, "promotional rate through 2026-12-31"),
        Gemini("gemini-3.5-flash-lite", 0.30m, 2.50m, 0.03m),
        Gemini("gemini-3.5-flash", 1.50m, 9.00m, 0.15m),
        Gemini("gemini-3.1-flash-lite", 0.25m, 1.50m, 0.025m, "text/image/video tier; audio is higher"),
        Gemini("gemini-3.1-pro", 2.00m, 12.00m, 0.20m, "prompts <=200k tokens; larger prompts cost more"),
        Gemini("gemini-2.5-pro", 1.25m, 10.00m, 0.125m, "prompts <=200k tokens; larger prompts cost more"),
        Gemini("gemini-2.5-flash-lite", 0.10m, 0.40m, 0.01m),
        Gemini("gemini-2.5-flash", 0.30m, 2.50m, 0.03m),
    ];

    /// <summary>Every built-in entry, across all providers.</summary>
    public static IReadOnlyList<ModelPrice> All { get; } = [.. Anthropic, .. OpenAI, .. Google];

    private static ModelPrice Claude(string model, decimal input, decimal output) => new()
    {
        Model = model,
        InputPerMillion = input,
        OutputPerMillion = output,
        CachedInputPerMillion = input * 0.10m,
        CacheWritePerMillion = input * 1.25m,
        Currency = "USD",
        LastVerified = AnthropicVerified,
        Source = AnthropicSource,
    };

    private static ModelPrice OpenAi(string model, decimal input, decimal output, decimal? cached = null) => new()
    {
        Model = model,
        InputPerMillion = input,
        OutputPerMillion = output,
        CachedInputPerMillion = cached,
        Currency = "USD",
        LastVerified = OpenAiVerified,
        Source = OpenAiSource,
    };

    private static ModelPrice Gemini(
        string model,
        decimal input,
        decimal output,
        decimal? cached = null,
        string? caveat = null) => new()
    {
        Model = model,
        InputPerMillion = input,
        OutputPerMillion = output,
        CachedInputPerMillion = cached,
        Currency = "USD",
        LastVerified = GoogleVerified,
        Source = caveat is null ? GoogleSource : $"{GoogleSource} ({caveat})",
    };
}
