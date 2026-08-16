using Microsoft.Extensions.AI;

namespace ModelMux;

/// <summary>
/// Turns a <see cref="ModelProfile"/> into a usable <see cref="IChatClient"/>.
/// </summary>
/// <remarks>
/// Implement this to teach ModelMux about a provider it doesn't ship with, then register
/// it via <c>AddProvider</c>. ModelMux deliberately does not define its own chat
/// abstraction — <see cref="IChatClient"/> from <c>Microsoft.Extensions.AI</c> is the
/// ecosystem standard, and everything downstream (middleware, tools, evaluation) already
/// speaks it.
/// </remarks>
public interface IChatProvider
{
    /// <summary>
    /// Provider name matched against <see cref="ModelProfile.Provider"/>, case-insensitively.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Creates a client for the profile. Called once per profile and cached, so it is fine
    /// for this to be relatively expensive.
    /// </summary>
    /// <exception cref="ModelMuxConfigurationException">
    /// The profile is missing something the provider requires, such as an API key.
    /// </exception>
    IChatClient CreateClient(string profileName, ModelProfile profile);
}
