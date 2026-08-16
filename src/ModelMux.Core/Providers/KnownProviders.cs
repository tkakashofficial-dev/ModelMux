namespace ModelMux.Providers;

/// <summary>
/// The providers ModelMux registers out of the box.
/// </summary>
/// <remarks>
/// All of them speak the OpenAI chat-completions protocol, so they share one implementation and
/// differ only in endpoint and whether a credential is required. Endpoints were taken from each
/// vendor's own documentation on 2026-08-16.
/// </remarks>
public static class KnownProviders
{
    /// <summary>Canonical provider name for OpenAI.</summary>
    public const string OpenAI = "OpenAI";

    /// <summary>Canonical provider name for Google Gemini.</summary>
    public const string Gemini = "Gemini";

    /// <summary>Canonical provider name for a local Ollama server.</summary>
    public const string Ollama = "Ollama";

    /// <summary>Canonical provider name for xAI Grok.</summary>
    public const string Grok = "Grok";

    /// <summary>
    /// Gemini's OpenAI-compatible endpoint.
    /// See https://ai.google.dev/gemini-api/docs/openai
    /// </summary>
    public static Uri GeminiEndpoint { get; } =
        new("https://generativelanguage.googleapis.com/v1beta/openai/");

    /// <summary>
    /// Default local Ollama endpoint. Override per profile to reach Ollama on another host.
    /// See https://docs.ollama.com/openai
    /// </summary>
    public static Uri OllamaEndpoint { get; } = new("http://localhost:11434/v1/");

    /// <summary>
    /// xAI's OpenAI-compatible endpoint.
    /// See https://docs.x.ai/docs/api-reference
    /// </summary>
    public static Uri GrokEndpoint { get; } = new("https://api.x.ai/v1");

    /// <summary>Creates the built-in provider set.</summary>
    public static IReadOnlyList<IChatProvider> CreateDefaults() =>
    [
        // Endpoint left null so the OpenAI SDK's own default is used and stays correct
        // if OpenAI moves it.
        new OpenAICompatibleProvider(OpenAI),

        new OpenAICompatibleProvider(Gemini, GeminiEndpoint),

        new OpenAICompatibleProvider(Grok, GrokEndpoint),

        // A local Ollama server ignores credentials, but the OpenAI client still insists
        // on a non-empty one.
        new OpenAICompatibleProvider(
            Ollama,
            OllamaEndpoint,
            requiresApiKey: false,
            unauthenticatedPlaceholder: "ollama"),
    ];
}
