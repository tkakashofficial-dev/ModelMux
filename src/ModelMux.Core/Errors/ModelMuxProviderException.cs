namespace ModelMux;

/// <summary>
/// Why a provider call failed, in terms an application can act on without knowing which
/// vendor was behind the request.
/// </summary>
public enum AiErrorCategory
{
    /// <summary>The failure could not be classified. Inspect <see cref="Exception.InnerException"/>.</summary>
    Unknown = 0,

    /// <summary>Credentials were missing, malformed, or rejected. Retrying will not help.</summary>
    AuthenticationFailure,

    /// <summary>The provider's rate limit was hit. Retrying after a delay usually helps.</summary>
    RateLimit,

    /// <summary>The request timed out.</summary>
    Timeout,

    /// <summary>The provider is down, overloaded, or unreachable.</summary>
    ProviderUnavailable,

    /// <summary>The request was malformed or rejected. Retrying the same request will not help.</summary>
    InvalidRequest,

    /// <summary>The model does not support something the request asked for.</summary>
    UnsupportedCapability,

    /// <summary>The provider's content filter rejected the request or response.</summary>
    ContentFiltered,
}

/// <summary>
/// A provider call failed, classified into a vendor-neutral <see cref="AiErrorCategory"/>.
/// </summary>
/// <remarks>
/// <para>
/// The point is that callers can write <c>catch (ModelMuxProviderException ex) when
/// (ex.IsRetryable)</c> without referencing an OpenAI or Gemini exception type. The original
/// exception is always preserved as <see cref="Exception.InnerException"/> — classification
/// adds information, it never hides any.
/// </para>
/// </remarks>
public sealed class ModelMuxProviderException : Exception
{
    /// <summary>Creates a classified provider failure.</summary>
    public ModelMuxProviderException(
        string message,
        string profileName,
        string provider,
        string model,
        AiErrorCategory category,
        bool isRetryable,
        int? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProfileName = profileName;
        Provider = provider;
        Model = model;
        Category = category;
        IsRetryable = isRetryable;
        StatusCode = statusCode;
    }

    /// <summary>Profile that routed the failed call.</summary>
    public string ProfileName { get; }

    /// <summary>Provider that served it, e.g. <c>Gemini</c>.</summary>
    public string Provider { get; }

    /// <summary>Model that was requested.</summary>
    public string Model { get; }

    /// <summary>Vendor-neutral classification of the failure.</summary>
    public AiErrorCategory Category { get; }

    /// <summary>
    /// Whether retrying the identical request could plausibly succeed. True for rate limits,
    /// timeouts, and provider outages; false for auth and malformed-request failures.
    /// </summary>
    public bool IsRetryable { get; }

    /// <summary>HTTP status code, when the provider surfaced one.</summary>
    public int? StatusCode { get; }
}

/// <summary>
/// A profile was asked for something its model does not support.
/// </summary>
/// <remarks>
/// Thrown before any network call, so an unsupported request fails fast and cheaply rather
/// than after a round-trip and a confusing provider error.
/// </remarks>
public sealed class UnsupportedCapabilityException : Exception
{
    /// <summary>Creates the exception for a specific unsupported capability.</summary>
    public UnsupportedCapabilityException(string profileName, string model, string capability)
        : base($"Profile '{profileName}' uses model '{model}', which is not configured to support "
               + $"{capability}. Either use a profile whose model supports it, or set "
               + $"ModelMux:Profiles:{profileName}:Capabilities:{capability} to true if the model "
               + "does support it and ModelMux's defaults are wrong.")
    {
        ProfileName = profileName;
        Model = model;
        Capability = capability;
    }

    /// <summary>Profile that was asked.</summary>
    public string ProfileName { get; }

    /// <summary>Model behind the profile.</summary>
    public string Model { get; }

    /// <summary>Capability that is not available, e.g. <c>Vision</c>.</summary>
    public string Capability { get; }
}
