namespace ModelMux;

/// <summary>
/// A logical model, named by what it is <i>for</i> rather than by which vendor serves it.
/// </summary>
/// <remarks>
/// <para>
/// Application code asks for a profile — <c>"fast"</c>, <c>"smart"</c>, <c>"private"</c> —
/// and never names a provider. Re-pointing <c>"fast"</c> from Gemini to a self-hosted model
/// is a configuration change, not a code change. That indirection is the whole point of
/// ModelMux.
/// </para>
/// </remarks>
public sealed class ModelProfile
{
    /// <summary>
    /// Provider that serves this profile, matched case-insensitively against a registered
    /// provider name (<c>OpenAI</c>, <c>Gemini</c>, <c>Ollama</c>, or one you register).
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Provider-specific model id, e.g. <c>gemini-2.5-flash</c> or <c>gpt-5-mini</c>.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Overrides the provider's default endpoint. Set this to point a profile at a
    /// self-hosted or proxied server that speaks the provider's protocol — a vLLM box,
    /// LM Studio, or a rented GPU — without any code change.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Name of the environment variable holding the API key. <b>Prefer this over
    /// <see cref="ApiKey"/></b> so credentials never enter configuration files or source control.
    /// </summary>
    public string? ApiKeyEnvironmentVariable { get; set; }

    /// <summary>
    /// Literal API key. Convenient for a local spike, but it lands in <c>appsettings.json</c>
    /// and therefore in git — use <see cref="ApiKeyEnvironmentVariable"/> or user-secrets for
    /// anything real. When both are set, the environment variable wins.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional description, surfaced in diagnostics and error messages.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Overrides the provider's default capabilities for this model. Leave null to accept the
    /// provider's defaults — ModelMux cannot know every model that will ever ship, so this is
    /// the escape hatch for when its assumptions are wrong for yours.
    /// </summary>
    public ModelCapabilities? Capabilities { get; set; }

    /// <summary>
    /// Resolves the API key: environment variable first, literal second.
    /// Returns null when neither is configured.
    /// </summary>
    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKeyEnvironmentVariable))
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment;
            }
        }

        return string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey;
    }
}
