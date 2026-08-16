namespace ModelMux;

/// <summary>
/// What a profile's model can do.
/// </summary>
/// <remarks>
/// <para>
/// Providers are not interchangeable in practice: a local 3B model does not do tool calling,
/// and not every model accepts images. Capabilities let application code check before it asks,
/// so an unsupported request fails immediately with a clear message rather than as a confusing
/// provider error after a round-trip.
/// </para>
/// <para>
/// Defaults come from the provider and can be overridden per profile in configuration, because
/// ModelMux cannot know every model that will ever exist — and a hardcoded list would be wrong
/// within weeks.
/// </para>
/// </remarks>
public sealed class ModelCapabilities
{
    /// <summary>Token-by-token streaming responses.</summary>
    public bool Streaming { get; set; } = true;

    /// <summary>Function/tool calling.</summary>
    public bool ToolCalling { get; set; } = true;

    /// <summary>Schema-constrained JSON output.</summary>
    public bool StructuredOutput { get; set; } = true;

    /// <summary>Image inputs.</summary>
    public bool Vision { get; set; }

    /// <summary>Maximum input tokens, when known.</summary>
    public int? ContextWindow { get; set; }

    /// <summary>Returns whether the named capability is available, matched case-insensitively.</summary>
    public bool Supports(string capability) => capability?.ToLowerInvariant() switch
    {
        "streaming" => Streaming,
        "toolcalling" => ToolCalling,
        "structuredoutput" => StructuredOutput,
        "vision" => Vision,
        _ => false,
    };

    /// <summary>
    /// Conservative defaults for a model served over the OpenAI protocol. Streaming, tools, and
    /// structured output are near-universal there; vision is not, so it defaults to false and
    /// is opted into per profile.
    /// </summary>
    public static ModelCapabilities OpenAiCompatibleDefaults() => new();
}
