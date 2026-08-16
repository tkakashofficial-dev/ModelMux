using Microsoft.Extensions.AI;

namespace ModelMux;

/// <summary>
/// Typed responses through a profile, so AI calls aren't reduced to <c>string -&gt; string</c>.
/// </summary>
/// <remarks>
/// The schema work is done by <c>Microsoft.Extensions.AI</c>, which already derives a JSON
/// schema from the target type and validates the response. ModelMux adds the profile
/// indirection and a capability check, so a model that can't do structured output fails
/// immediately with a clear message rather than returning unparseable prose.
/// </remarks>
public static class StructuredOutputExtensions
{
    /// <summary>
    /// Sends <paramref name="prompt"/> through a profile and deserialises the reply into
    /// <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Shape to return. Its schema constrains the model's output.</typeparam>
    /// <param name="mux">The router.</param>
    /// <param name="prompt">The instruction.</param>
    /// <param name="profileName">Profile to use, or null for the default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnsupportedCapabilityException">
    /// The profile's model is not configured for structured output.
    /// </exception>
    /// <example>
    /// <code>
    /// record Analysis(string Category, double Confidence, string Summary);
    ///
    /// var result = await mux.GetStructuredResponseAsync&lt;Analysis&gt;(
    ///     "Analyse this support ticket: ...", profileName: "smart");
    /// </code>
    /// </example>
    public static async Task<T?> GetStructuredResponseAsync<T>(
        this IModelMux mux,
        string prompt,
        string? profileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mux);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        // Fail before the network call rather than after an unparseable reply.
        mux.RequireCapability(nameof(ModelCapabilities.StructuredOutput), profileName);

        var response = await mux.GetClient(profileName)
            .GetResponseAsync<T>(prompt, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return response.TryGetResult(out var result) ? result : default;
    }

    /// <summary>
    /// Streams a response through a profile, checking first that the model supports streaming.
    /// </summary>
    /// <param name="mux">The router.</param>
    /// <param name="prompt">The instruction.</param>
    /// <param name="profileName">Profile to use, or null for the default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="UnsupportedCapabilityException">
    /// The profile's model is not configured for streaming.
    /// </exception>
    public static IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        this IModelMux mux,
        string prompt,
        string? profileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mux);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        mux.RequireCapability(nameof(ModelCapabilities.Streaming), profileName);

        return mux.GetClient(profileName).GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: cancellationToken);
    }
}
