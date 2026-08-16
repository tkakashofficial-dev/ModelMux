using System.ClientModel;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ModelMux.Errors;

/// <summary>
/// Translates provider-specific exceptions into <see cref="ModelMuxProviderException"/>.
/// </summary>
/// <remarks>
/// Registered as the innermost decorator so it sees raw provider failures before anything else
/// wraps them. The original exception is always preserved as the inner exception — this
/// classifies, it does not swallow.
/// </remarks>
internal sealed class ErrorMappingChatClient(
    IChatClient innerClient,
    string profileName,
    string provider,
    string model) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ShouldMap(ex, cancellationToken))
        {
            throw Map(ex);
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var enumerator = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                ChatResponseUpdate update;

                // A yield can't sit inside a try/catch, so advancing is wrapped separately.
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (Exception ex) when (ShouldMap(ex, cancellationToken))
                {
                    throw Map(ex);
                }

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Caller-requested cancellation is not a provider failure and must stay an
    /// <see cref="OperationCanceledException"/> so <c>catch (OperationCanceledException)</c>
    /// and cancellation-aware plumbing keep working.
    /// </summary>
    private static bool ShouldMap(Exception ex, CancellationToken cancellationToken) =>
        !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        && ex is not ModelMuxProviderException;

    private ModelMuxProviderException Map(Exception ex)
    {
        var (category, retryable, status) = Classify(ex);

        var message =
            $"Call to provider '{provider}' (model '{model}', profile '{profileName}') failed: "
            + $"{category}{(status is null ? string.Empty : $" [HTTP {status}]")}. {ex.Message}";

        return new ModelMuxProviderException(
            message, profileName, provider, model, category, retryable, status, ex);
    }

    private static (AiErrorCategory Category, bool Retryable, int? Status) Classify(Exception ex) =>
        ex switch
        {
            // The OpenAI SDK and everything built on System.ClientModel surface HTTP failures here.
            ClientResultException clientResult => FromStatusCode(clientResult.Status),

            HttpRequestException { StatusCode: { } code } => FromStatusCode((int)code),
            HttpRequestException => (AiErrorCategory.ProviderUnavailable, true, null),

            // A timeout cancels the token internally, so an OperationCanceledException that
            // reaches here (caller token not cancelled) is a timeout, not a user cancellation.
            TaskCanceledException or TimeoutException or OperationCanceledException =>
                (AiErrorCategory.Timeout, true, null),

            _ => (AiErrorCategory.Unknown, false, null),
        };

    private static (AiErrorCategory Category, bool Retryable, int? Status) FromStatusCode(int status) =>
        status switch
        {
            (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden =>
                (AiErrorCategory.AuthenticationFailure, false, status),

            (int)HttpStatusCode.TooManyRequests =>
                (AiErrorCategory.RateLimit, true, status),

            (int)HttpStatusCode.RequestTimeout =>
                (AiErrorCategory.Timeout, true, status),

            // 422 is what most providers use for a content-policy rejection.
            (int)HttpStatusCode.UnprocessableEntity =>
                (AiErrorCategory.ContentFiltered, false, status),

            (int)HttpStatusCode.NotFound or (int)HttpStatusCode.BadRequest =>
                (AiErrorCategory.InvalidRequest, false, status),

            >= 500 => (AiErrorCategory.ProviderUnavailable, true, status),

            >= 400 => (AiErrorCategory.InvalidRequest, false, status),

            _ => (AiErrorCategory.Unknown, false, status),
        };
}

/// <summary>Wraps every routed client so provider failures arrive classified.</summary>
internal sealed class ErrorMappingDecorator : IChatClientDecorator
{
    public IChatClient Decorate(string profileName, ModelProfile profile, IChatClient client) =>
        new ErrorMappingChatClient(client, profileName, profile.Provider, profile.Model);
}
