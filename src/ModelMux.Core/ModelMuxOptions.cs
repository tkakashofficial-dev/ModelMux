namespace ModelMux;

/// <summary>Configuration for ModelMux, bound from the <c>ModelMux</c> section by default.</summary>
/// <example>
/// <code language="json">
/// {
///   "ModelMux": {
///     "DefaultProfile": "fast",
///     "Profiles": {
///       "fast":    { "Provider": "Gemini", "Model": "gemini-2.5-flash", "ApiKeyEnvironmentVariable": "GEMINI_API_KEY" },
///       "smart":   { "Provider": "OpenAI", "Model": "gpt-5",            "ApiKeyEnvironmentVariable": "OPENAI_API_KEY" },
///       "private": { "Provider": "Ollama", "Model": "llama3" }
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class ModelMuxOptions
{
    /// <summary>Configuration section name bound by the <c>IConfiguration</c> overload.</summary>
    public const string SectionName = "ModelMux";

    /// <summary>
    /// Profile used when callers don't name one. Optional when exactly one profile is
    /// configured — that profile becomes the default.
    /// </summary>
    public string? DefaultProfile { get; set; }

    /// <summary>
    /// Named profiles. Keys are matched case-insensitively so <c>"Fast"</c> and <c>"fast"</c>
    /// resolve to the same profile.
    /// </summary>
    public IDictionary<string, ModelProfile> Profiles { get; set; } =
        new Dictionary<string, ModelProfile>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the effective default profile name, or null when it cannot be determined.
    /// </summary>
    public string? ResolveDefaultProfileName()
    {
        if (!string.IsNullOrWhiteSpace(DefaultProfile))
        {
            return DefaultProfile;
        }

        // A single-profile setup has an unambiguous default; requiring it to be named
        // would be pure ceremony.
        return Profiles.Count == 1 ? Profiles.Keys.First() : null;
    }
}
