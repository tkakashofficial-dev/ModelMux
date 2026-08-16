using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ModelMux.Cost.Tests;

/// <summary>
/// A stand-in <see cref="IChatClient"/> so the middleware can be tested without any
/// network calls or API keys. Every test in this project runs offline.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly Func<ChatResponse>? _responseFactory;
    private readonly Exception? _throwOnCall;
    private readonly IReadOnlyList<ChatResponseUpdate>? _streamingUpdates;
    private readonly Exception? _throwMidStream;

    public int CallCount { get; private set; }

    public ChatClientMetadata Metadata { get; init; } = new("fake-provider", null, "fake-model");

    private FakeChatClient(
        Func<ChatResponse>? responseFactory = null,
        Exception? throwOnCall = null,
        IReadOnlyList<ChatResponseUpdate>? streamingUpdates = null,
        Exception? throwMidStream = null)
    {
        _responseFactory = responseFactory;
        _throwOnCall = throwOnCall;
        _streamingUpdates = streamingUpdates;
        _throwMidStream = throwMidStream;
    }

    /// <summary>A client that returns a response with the given reported token counts.</summary>
    public static FakeChatClient WithUsage(
        long inputTokens,
        long outputTokens,
        string modelId = "claude-opus-5",
        string responseText = "hello",
        IDictionary<string, long>? additionalCounts = null)
    {
        return new FakeChatClient(() =>
        {
            var usage = new UsageDetails
            {
                InputTokenCount = inputTokens,
                OutputTokenCount = outputTokens,
                TotalTokenCount = inputTokens + outputTokens,
            };

            if (additionalCounts is not null)
            {
                usage.AdditionalCounts = [];
                foreach (var pair in additionalCounts)
                {
                    usage.AdditionalCounts[pair.Key] = pair.Value;
                }
            }

            return new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                ModelId = modelId,
                ResponseId = "resp-1",
                Usage = usage,
            };
        });
    }

    /// <summary>A client that reports no usage at all, as local models commonly do.</summary>
    public static FakeChatClient WithoutUsage(string modelId = "llama3", string responseText = "hello there")
    {
        return new FakeChatClient(() =>
            new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                ModelId = modelId,
            });
    }

    /// <summary>A client that throws instead of answering.</summary>
    public static FakeChatClient ThatThrows(Exception exception) => new(throwOnCall: exception);

    /// <summary>A client that streams the given updates.</summary>
    public static FakeChatClient Streaming(params ChatResponseUpdate[] updates) =>
        new(streamingUpdates: updates);

    /// <summary>A client that yields one update, then throws.</summary>
    public static FakeChatClient StreamingThatThrows(ChatResponseUpdate first, Exception exception) =>
        new(streamingUpdates: [first], throwMidStream: exception);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (_throwOnCall is not null)
        {
            throw _throwOnCall;
        }

        return Task.FromResult(_responseFactory!());
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        CallCount++;

        foreach (var update in _streamingUpdates ?? [])
        {
            await Task.Yield();
            yield return update;
        }

        if (_throwMidStream is not null)
        {
            throw _throwMidStream;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(ChatClientMetadata) ? Metadata : null;

    public void Dispose()
    {
    }
}

/// <summary>An <see cref="IUsageStore"/> that always throws, to prove telemetry can't break a call.</summary>
internal sealed class ThrowingUsageStore : IUsageStore
{
    public ValueTask AddAsync(UsageRecord record, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("store is down");
}
