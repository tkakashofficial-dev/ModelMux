using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ModelMux;

/// <summary>Default <see cref="IModelMux"/> implementation.</summary>
/// <remarks>
/// Clients are created lazily on first use and cached for the lifetime of the router, because
/// provider clients own HTTP connection pools and creating one per request is a well-known way
/// to exhaust sockets.
/// </remarks>
public sealed class ModelMuxRouter : IModelMux, IDisposable
{
    private readonly ModelMuxOptions _options;
    private readonly IReadOnlyDictionary<string, IChatProvider> _providers;
    private readonly IReadOnlyList<IChatClientDecorator> _decorators;
    private readonly ConcurrentDictionary<string, Lazy<IChatClient>> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    /// <summary>Creates a router over the configured profiles and registered providers.</summary>
    public ModelMuxRouter(
        IOptions<ModelMuxOptions> options,
        IEnumerable<IChatProvider> providers,
        IEnumerable<IChatClientDecorator>? decorators = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);

        _decorators = decorators is null ? [] : [.. decorators];

        _options = options.Value;

        var byName = new Dictionary<string, IChatProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            // Later registrations win, so an application can replace a built-in provider
            // by registering its own under the same name.
            byName[provider.Name] = provider;
        }

        _providers = byName;

        DefaultProfileName = _options.ResolveDefaultProfileName()
            ?? throw new ModelMuxConfigurationException(
                _options.Profiles.Count == 0
                    ? "ModelMux has no profiles configured. Add at least one under ModelMux:Profiles."
                    : "ModelMux:DefaultProfile is not set, and there is more than one profile so a "
                      + $"default cannot be inferred. Set it to one of: {string.Join(", ", _options.Profiles.Keys)}.");

        if (!_options.Profiles.ContainsKey(DefaultProfileName))
        {
            throw new ModelMuxConfigurationException(
                $"ModelMux:DefaultProfile is '{DefaultProfileName}', but no such profile is configured. "
                + $"Available profiles: {string.Join(", ", _options.Profiles.Keys)}.");
        }
    }

    /// <inheritdoc />
    public string DefaultProfileName { get; }

    /// <inheritdoc />
    public IReadOnlyCollection<string> ProfileNames => (IReadOnlyCollection<string>)_options.Profiles.Keys;

    /// <inheritdoc />
    public ModelProfile GetProfile(string profileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        if (!_options.Profiles.TryGetValue(profileName, out var profile))
        {
            throw new ModelMuxConfigurationException(
                $"No ModelMux profile named '{profileName}'. "
                + $"Available profiles: {string.Join(", ", _options.Profiles.Keys)}.");
        }

        return profile;
    }

    /// <inheritdoc />
    public ModelCapabilities GetCapabilities(string? profileName = null)
    {
        var name = string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName;
        var profile = GetProfile(name);

        // Everything ModelMux ships speaks the OpenAI protocol, so its defaults are the
        // sensible baseline until a profile says otherwise.
        return profile.Capabilities ?? ModelCapabilities.OpenAiCompatibleDefaults();
    }

    /// <inheritdoc />
    public void RequireCapability(string capability, string? profileName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        var name = string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName;

        if (!GetCapabilities(name).Supports(capability))
        {
            throw new UnsupportedCapabilityException(name, GetProfile(name).Model, capability);
        }
    }

    /// <inheritdoc />
    public IChatClient GetClient(string? profileName = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var name = string.IsNullOrWhiteSpace(profileName) ? DefaultProfileName : profileName;

        // Lazy with ExecutionAndPublication so a provider's client is built exactly once even
        // under concurrent first-use, which matters when construction opens connections.
        var lazy = _clients.GetOrAdd(
            name,
            key => new Lazy<IChatClient>(() => CreateClient(key), LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    private IChatClient CreateClient(string profileName)
    {
        var profile = GetProfile(profileName);

        if (string.IsNullOrWhiteSpace(profile.Provider))
        {
            throw new ModelMuxConfigurationException(
                $"Profile '{profileName}' does not specify a Provider. "
                + $"Set ModelMux:Profiles:{profileName}:Provider to one of: {KnownProviderNames()}.");
        }

        if (!_providers.TryGetValue(profile.Provider, out var provider))
        {
            throw new ModelMuxConfigurationException(
                $"Profile '{profileName}' uses provider '{profile.Provider}', which is not registered. "
                + $"Registered providers: {KnownProviderNames()}. "
                + "Register a custom provider with AddModelMux(...).AddProvider(...).");
        }

        var client = provider.CreateClient(profileName, profile);

        // Registration order, each wrapping the previous, so the last registered ends up
        // outermost and observes everything the earlier ones do.
        foreach (var decorator in _decorators)
        {
            client = decorator.Decorate(profileName, profile, client)
                ?? throw new ModelMuxConfigurationException(
                    $"Decorator '{decorator.GetType().Name}' returned null for profile '{profileName}'. "
                    + "A decorator must return a client — return the one it was given to opt out.");
        }

        return client;
    }

    private string KnownProviderNames() =>
        _providers.Count == 0 ? "(none)" : string.Join(", ", _providers.Keys.Order());

    /// <summary>Disposes every client this router created.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var lazy in _clients.Values)
        {
            // Only dispose clients that were actually built; touching .Value here would
            // construct one purely in order to throw it away.
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }

        _clients.Clear();
    }
}
