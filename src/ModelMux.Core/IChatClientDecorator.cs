using Microsoft.Extensions.AI;

namespace ModelMux;

/// <summary>
/// Wraps every client the router creates, so cross-cutting behaviour can be added without
/// each provider knowing about it.
/// </summary>
/// <remarks>
/// <para>
/// Decorators are applied in registration order, each wrapping the previous one, so the
/// <b>last registered decorator is the outermost</b> and sees every call first. That matters
/// for anything measuring duration: register cost tracking last and it times everything
/// inside it, including retries added by an earlier decorator.
/// </para>
/// <para>
/// This is the hook <c>ModelMux.Cost</c> uses. Fallback and caching will use the same one.
/// </para>
/// </remarks>
public interface IChatClientDecorator
{
    /// <summary>
    /// Returns a client wrapping <paramref name="client"/>, or the client unchanged when this
    /// decorator does not apply to the profile.
    /// </summary>
    /// <param name="profileName">Profile the client serves, for attribution and diagnostics.</param>
    /// <param name="profile">The profile's configuration.</param>
    /// <param name="client">The client to wrap.</param>
    IChatClient Decorate(string profileName, ModelProfile profile, IChatClient client);
}
