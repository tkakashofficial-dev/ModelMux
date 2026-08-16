using Microsoft.Extensions.AI;

namespace ModelMux;

/// <summary>
/// Resolves logical model profiles to live <see cref="IChatClient"/> instances.
/// </summary>
/// <remarks>
/// Inject this only when a component genuinely needs to choose between profiles at runtime.
/// Most application code should inject <see cref="IChatClient"/> directly — it is registered
/// as the default profile — and stay unaware that profiles exist at all.
/// </remarks>
public interface IModelMux
{
    /// <summary>Name of the profile used when a caller doesn't specify one.</summary>
    string DefaultProfileName { get; }

    /// <summary>All configured profile names.</summary>
    IReadOnlyCollection<string> ProfileNames { get; }

    /// <summary>
    /// Returns the client for <paramref name="profileName"/>, or the default profile when null.
    /// </summary>
    /// <remarks>
    /// Clients are created once per profile and reused, so this is cheap to call per request.
    /// </remarks>
    /// <exception cref="ModelMuxConfigurationException">
    /// No such profile, or the profile cannot be turned into a client.
    /// </exception>
    IChatClient GetClient(string? profileName = null);

    /// <summary>Returns the configuration behind a profile, for diagnostics and health checks.</summary>
    /// <exception cref="ModelMuxConfigurationException">No such profile.</exception>
    ModelProfile GetProfile(string profileName);

    /// <summary>
    /// Returns what a profile's model can do — the profile's own override if set, otherwise the
    /// provider's defaults.
    /// </summary>
    /// <exception cref="ModelMuxConfigurationException">No such profile.</exception>
    ModelCapabilities GetCapabilities(string? profileName = null);

    /// <summary>
    /// Throws if the profile does not support the named capability. Call this before issuing a
    /// request that depends on one, to fail fast instead of after a round-trip.
    /// </summary>
    /// <exception cref="UnsupportedCapabilityException">The capability is not available.</exception>
    void RequireCapability(string capability, string? profileName = null);
}
