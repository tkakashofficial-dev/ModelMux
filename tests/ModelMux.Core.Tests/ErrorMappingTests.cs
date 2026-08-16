using System.ClientModel;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ModelMux.Core.Tests;

/// <summary>
/// Callers should be able to write <c>catch (ModelMuxProviderException ex) when (ex.IsRetryable)</c>
/// without ever naming a vendor's exception type.
/// </summary>
public class ErrorMappingTests
{
    private sealed class ThrowingProvider(Exception exception) : IChatProvider
    {
        public string Name => "Gemini";

        public IChatClient CreateClient(string profileName, ModelProfile profile) =>
            new ThrowingClient(exception);
    }

    private sealed class ThrowingClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw exception;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw exception;
#pragma warning disable CS0162 // Required to make this an iterator method.
            yield break;
#pragma warning restore CS0162
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static IChatClient ClientThatThrows(Exception exception)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ModelMux:Profiles:fast:Provider"] = "Gemini",
            ["ModelMux:Profiles:fast:Model"] = "gemini-2.5-flash",
        }).Build();

        var services = new ServiceCollection();
        services.AddModelMux(config).AddProvider(new ThrowingProvider(exception));

        return services.BuildServiceProvider().GetRequiredService<IChatClient>();
    }

    private static async Task<ModelMuxProviderException> CaptureAsync(Exception thrown)
    {
        var client = ClientThatThrows(thrown);

        return await Assert.ThrowsAsync<ModelMuxProviderException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));
    }

    [Theory]
    [InlineData(401, AiErrorCategory.AuthenticationFailure, false)]
    [InlineData(403, AiErrorCategory.AuthenticationFailure, false)]
    [InlineData(429, AiErrorCategory.RateLimit, true)]
    [InlineData(408, AiErrorCategory.Timeout, true)]
    [InlineData(422, AiErrorCategory.ContentFiltered, false)]
    [InlineData(400, AiErrorCategory.InvalidRequest, false)]
    [InlineData(404, AiErrorCategory.InvalidRequest, false)]
    [InlineData(500, AiErrorCategory.ProviderUnavailable, true)]
    [InlineData(503, AiErrorCategory.ProviderUnavailable, true)]
    public async Task Http_status_codes_map_to_categories_and_retryability(
        int status,
        AiErrorCategory expected,
        bool retryable)
    {
        var ex = await CaptureAsync(
            new HttpRequestException($"boom {status}", null, (HttpStatusCode)status));

        Assert.Equal(expected, ex.Category);
        Assert.Equal(retryable, ex.IsRetryable);
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task ClientResultException_from_the_OpenAI_SDK_is_classified()
    {
        // System.ClientModel is what the OpenAI SDK and everything built on it throws.
        var ex = await CaptureAsync(new ClientResultException("upstream said no"));

        Assert.Contains("upstream said no", ex.Message, StringComparison.Ordinal);
        Assert.Equal("Gemini", ex.Provider);
    }

    [Fact]
    public async Task Network_failures_are_treated_as_provider_unavailable_and_retryable()
    {
        var ex = await CaptureAsync(new HttpRequestException("connection refused"));

        Assert.Equal(AiErrorCategory.ProviderUnavailable, ex.Category);
        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task Timeouts_are_retryable()
    {
        var ex = await CaptureAsync(new TimeoutException("took too long"));

        Assert.Equal(AiErrorCategory.Timeout, ex.Category);
        Assert.True(ex.IsRetryable);
    }

    [Fact]
    public async Task The_original_exception_and_the_routing_context_are_preserved()
    {
        // Classification must add information, never hide it.
        var original = new HttpRequestException(
            "upstream detail", null, HttpStatusCode.TooManyRequests);

        var ex = await CaptureAsync(original);

        Assert.Same(original, ex.InnerException);
        Assert.Equal("fast", ex.ProfileName);
        Assert.Equal("Gemini", ex.Provider);
        Assert.Equal("gemini-2.5-flash", ex.Model);
        Assert.Contains("upstream detail", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unrecognised_failures_are_Unknown_and_not_retryable()
    {
        // Guessing "retryable" on an unclassified error would invite infinite retry loops.
        var ex = await CaptureAsync(new InvalidOperationException("something odd"));

        Assert.Equal(AiErrorCategory.Unknown, ex.Category);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task Caller_cancellation_stays_an_OperationCanceledException()
    {
        // Cancellation is not a provider failure; rewriting it would break every
        // `catch (OperationCanceledException)` in the calling application.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var client = ClientThatThrows(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")], cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Streaming_failures_are_mapped_too()
    {
        var client = ClientThatThrows(
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));

        var ex = await Assert.ThrowsAsync<ModelMuxProviderException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
            {
            }
        });

        Assert.Equal(AiErrorCategory.RateLimit, ex.Category);
    }
}
