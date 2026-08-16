using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ModelMux.Core.Tests;

/// <summary>
/// A provider that hands back a client tagged with its own identity, so tests can assert
/// which provider actually served a profile without making a network call.
/// </summary>
internal sealed class FakeProvider(string name) : IChatProvider
{
    public string Name { get; } = name;

    public int CreateCount { get; private set; }

    public IChatClient CreateClient(string profileName, ModelProfile profile)
    {
        CreateCount++;
        return new FakeChatClient(Name, profile.Model, profile.Endpoint);
    }
}

/// <summary>
/// Reaches through the decorator pipeline to the underlying fake.
/// </summary>
/// <remarks>
/// Routed clients are wrapped (error mapping always, cost tracking when registered), so a
/// direct cast would assert on the wrapper rather than the thing under test.
/// <c>GetService</c> is the standard way Microsoft.Extensions.AI exposes inner clients.
/// </remarks>
internal static class ChatClientTestExtensions
{
    public static FakeChatClient AsFake(this IChatClient client) =>
        client.GetService(typeof(FakeChatClient)) as FakeChatClient
        ?? throw new InvalidOperationException(
            $"No FakeChatClient found in the pipeline for {client.GetType().Name}.");
}

/// <summary>An <see cref="IChatClient"/> that echoes which provider and model produced it.</summary>
internal sealed class FakeChatClient(string providerName, string model, string? endpoint) : IChatClient
{
    public string ProviderName { get; } = providerName;
    public string Model { get; } = model;
    public string? Endpoint { get; } = endpoint;
    public bool IsDisposed { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, $"served by {ProviderName}/{Model}"))
        {
            ModelId = Model,
        });

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, $"served by {ProviderName}/{Model}");
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceType == typeof(ChatClientMetadata))
        {
            return new ChatClientMetadata(ProviderName, null, Model);
        }

        // Lets tests reach this instance through any wrapping decorators.
        return serviceType == typeof(FakeChatClient) ? this : null;
    }

    public void Dispose() => IsDisposed = true;
}
