namespace ModelMux.Cost.Estimation;

/// <summary>
/// Estimates token counts for providers that report none.
/// </summary>
/// <remarks>
/// Estimates are approximations, not measurements. Every record produced from an estimate
/// is flagged with <see cref="UsageRecord.IsEstimated"/> so reporting can separate the two —
/// silently mixing estimated and reported figures into one cost total makes the total a lie.
/// </remarks>
public interface ITokenEstimator
{
    /// <summary>Returns an approximate token count for <paramref name="text"/>.</summary>
    long EstimateTokens(string? text);
}

/// <summary>
/// Character-count heuristic (~4 characters per token), the rule of thumb that holds
/// reasonably for English prose across common BPE tokenizers.
/// </summary>
/// <remarks>
/// It is materially wrong for code, non-Latin scripts, and heavily punctuated text.
/// It exists so local models still produce a usable cost signal, not to replace a real
/// tokenizer. Register your own <see cref="ITokenEstimator"/> when you need better.
/// </remarks>
public sealed class HeuristicTokenEstimator : ITokenEstimator
{
    private const double CharactersPerToken = 4.0;

    /// <inheritdoc />
    public long EstimateTokens(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return (long)Math.Ceiling(text.Length / CharactersPerToken);
    }
}
