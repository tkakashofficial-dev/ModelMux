using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ModelMux.Providers;

/// <summary>
/// Serves any endpoint that speaks the OpenAI chat-completions protocol.
/// </summary>
/// <remarks>
/// <para>
/// The protocol has become the de facto standard, so a single implementation covers OpenAI
/// itself, Google Gemini and Ollama (both publish OpenAI-compatible endpoints), and
/// self-hosted runtimes such as vLLM, LM Studio, and LocalAI. Pointing a profile at a rented
/// GPU is then an <see cref="ModelProfile.Endpoint"/> change rather than new code.
/// </para>
/// <para>
/// Providers that need a genuinely different wire format â€” Anthropic's native API, AWS
/// Bedrock â€” get their own <see cref="IChatProvider"/> instead of being forced through here.
/// </para>
/// </remarks>
public sealed class OpenAICompatibleProvider : IChatProvider
{
    private readonly Uri? _defaultEndpoint;
    private readonly bool _requiresApiKey;
    private readonly string _unauthenticatedPlaceholder;

    /// <param name="name">Provider name matched against <see cref="ModelProfile.Provider"/>.</param>
    /// <param name="defaultEndpoint">
    /// Endpoint used when the profile doesn't override it. Null means the OpenAI SDK default.
    /// </param>
    /// <param name="requiresApiKey">
    /// False for endpoints that ignore credentials, such as a local Ollama server.
    /// </param>
    /// <param name="unauthenticatedPlaceholder">
    /// Sent when <paramref name="requiresApiKey"/> is false. The OpenAI client requires a
    /// non-empty credential even where the server ignores it.
    /// </param>
    public OpenAICompatibleProvider(
        string name,
        Uri? defaultEndpoint = null,
        bool requiresApiKey = true,
        string unauthenticatedPlaceholder = "not-required")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        _defaultEndpoint = defaultEndpoint;
        _requiresApiKey = requiresApiKey;
        _unauthenticatedPlaceholder = unauthenticatedPlaceholder;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public IChatClient CreateClient(string profileName, ModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (string.IsNullOrWhiteSpace(profile.Model))
        {
            throw new ModelMuxConfigurationException(
                $"Profile '{profileName}' does not specify a Model. "
                + $"Set ModelMux:Profiles:{profileName}:Model to a model id supported by {Name}.");
        }

        var apiKey = profile.ResolveApiKey();

        if (_requiresApiKey && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ModelMuxConfigurationException(
                $"Profile '{profileName}' uses provider '{Name}', which requires an API key, but none was found. "
                + $"Set ModelMux:Profiles:{profileName}:ApiKeyEnvironmentVariable to the name of an environment "
                + "variable holding the key (preferred), or ApiKey to the key itself (development only)."
                + (string.IsNullOrWhiteSpace(profile.ApiKeyEnvironmentVariable)
                    ? string.Empty
                    : $" Environment variable '{profile.ApiKeyEnvironmentVariable}' is not set or is empty."));
        }

        var endpoint = ResolveEndpoint(profileName, profile);

        var clientOptions = new OpenAIClientOptions();
        if (endpoint is not null)
        {
            clientOptions.Endpoint = endpoint;
        }

        var credential = new ApiKeyCredential(apiKey ?? _unauthenticatedPlaceholder);

        return new OpenAIClient(credential, clientOptions)
            .GetChatClient(profile.Model)
            .AsIChatClient();
    }

    private Uri? ResolveEndpoint(string profileName, ModelProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Endpoint))
        {
            return _defaultEndpoint;
        }

        if (!Uri.TryCreate(profile.Endpoint, UriKind.Absolute, out var endpoint))
        {
            throw new ModelMuxConfigurationException(
                $"Profile '{profileName}' has an Endpoint that is not an absolute URI: '{profile.Endpoint}'. "
                + "Expected something like 'https://my-host:8000/v1/'.");
        }

        return endpoint;
    }
}
