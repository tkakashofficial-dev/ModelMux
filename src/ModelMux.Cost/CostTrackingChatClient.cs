using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModelMux.Cost.Attribution;
using ModelMux.Cost.Estimation;
using ModelMux.Cost.Pricing;

namespace ModelMux.Cost;

/// <summary>
/// An <see cref="IChatClient"/> middleware that records token usage, cost, latency, and
/// attribution for every call passing through it, then delegates to the inner client.
/// </summary>
/// <remarks>
/// <para>
/// Recording never changes the outcome of a call. Failures in the usage store are logged
/// and swallowed â€” telemetry must not be able to break the caller's LLM request.
/// </para>
/// <para>
/// Register it with <c>ChatClientBuilder.UseCostTracking()</c> rather than constructing it
/// directly.
/// </para>
/// </remarks>
public sealed class CostTrackingChatClient : DelegatingChatClient
{
    // Providers surface cache accounting through UsageDetails.AdditionalCounts under
    // inconsistent key names. Match the ones in circulation rather than picking one.
    private static readonly string[] CachedInputKeys =
    [
        "cache_read_input_tokens",
        "CacheReadInputTokens",
        "cached_input_tokens",
        "InputTokenDetails.CachedTokens",
        "cached_tokens",
    ];

    private static readonly string[] CacheWriteKeys =
    [
        "cache_creation_input_tokens",
        "CacheCreationInputTokens",
        "cache_write_input_tokens",
    ];

    private readonly IUsageStore _store;
    private readonly ICostCalculator _costCalculator;
    private readonly ITokenEstimator _estimator;
    private readonly IUsageAttributionAccessor _attribution;
    private readonly CostTrackingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CostTrackingChatClient> _logger;

    /// <summary>
    /// Wraps <paramref name="innerClient"/> so its calls are recorded. Prefer
    /// <c>ChatClientBuilder.UseCostTracking()</c> over calling this directly.
    /// </summary>
    public CostTrackingChatClient(
        IChatClient innerClient,
        IUsageStore store,
        ICostCalculator costCalculator,
        ITokenEstimator estimator,
        IUsageAttributionAccessor attribution,
        IOptions<CostTrackingOptions> options,
        TimeProvider? timeProvider = null,
        ILogger<CostTrackingChatClient>? logger = null)
        : base(innerClient)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _costCalculator = costCalculator ?? throw new ArgumentNullException(nameof(costCalculator));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _attribution = attribution ?? throw new ArgumentNullException(nameof(attribution));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CostTrackingChatClient>.Instance;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }

        var startedAt = _timeProvider.GetUtcNow();
        var timestamp = _timeProvider.GetTimestamp();

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RecordAsync(
                new RecordContext
                {
                    StartedAt = startedAt,
                    Elapsed = _timeProvider.GetElapsedTime(timestamp),
                    RequestedModelId = options?.ModelId,
                    Messages = messages,
                    Streamed = false,
                    Exception = ex,
                }).ConfigureAwait(false);

            throw;
        }

        await RecordAsync(
            new RecordContext
            {
                StartedAt = startedAt,
                Elapsed = _timeProvider.GetElapsedTime(timestamp),
                RequestedModelId = options?.ModelId,
                Messages = messages,
                Streamed = false,
                Usage = response.Usage,
                ResponseModelId = response.ModelId,
                ResponseId = response.ResponseId,
                CompletionText = response.Text,
            }).ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            await foreach (var passthrough in base
                .GetStreamingResponseAsync(messages, options, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        var startedAt = _timeProvider.GetUtcNow();
        var timestamp = _timeProvider.GetTimestamp();

        // Usage arrives at the end of a stream, not up front, so it has to be accumulated.
        UsageDetails? usage = null;
        string? responseModelId = null;
        string? responseId = null;
        var completion = new StringBuilder();
        Exception? failure = null;

        var enumerator = base
            .GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                ChatResponseUpdate update;

                // A yield cannot live inside a try/catch, so advancing the source is
                // wrapped separately from handing the update to the consumer.
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    throw;
                }

                responseModelId ??= update.ModelId;
                responseId ??= update.ResponseId;

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        usage = Merge(usage, usageContent.Details);
                    }
                }

                if (_options.RecordPromptContent && completion.Length < _options.MaxRecordedContentLength)
                {
                    completion.Append(update.Text);
                }

                yield return update;
            }
        }
        finally
        {
            var elapsed = _timeProvider.GetElapsedTime(timestamp);

            await enumerator.DisposeAsync().ConfigureAwait(false);

            await RecordAsync(
                new RecordContext
                {
                    StartedAt = startedAt,
                    Elapsed = elapsed,
                    RequestedModelId = options?.ModelId,
                    Messages = messages,
                    Streamed = true,
                    Usage = usage,
                    ResponseModelId = responseModelId,
                    ResponseId = responseId,
                    CompletionText = completion.ToString(),
                    Exception = failure,
                }).ConfigureAwait(false);
        }
    }

    private async ValueTask RecordAsync(RecordContext context)
    {
        try
        {
            var record = BuildRecord(context);

            // Deliberately not the caller's token: a cancelled request still produced
            // usage that was billed, and dropping it would understate spend.
            await _store.AddAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Telemetry must never break the call it is measuring.
            _logger.LogError(ex, "ModelMux.Cost failed to record usage; the LLM call itself was unaffected.");
        }
    }

    private UsageRecord BuildRecord(RecordContext context)
    {
        // Prefer what the provider actually served over what was requested; they differ
        // when a gateway routes elsewhere or a fallback model handles the call.
        var modelId =
            context.ResponseModelId
            ?? context.RequestedModelId
            ?? MetadataModelId()
            ?? "unknown";

        var reportedInput = context.Usage?.InputTokenCount;
        var reportedOutput = context.Usage?.OutputTokenCount;

        var isEstimated = false;
        long inputTokens;
        long outputTokens;

        if (reportedInput is null && reportedOutput is null && _options.EstimateTokensWhenMissing)
        {
            // Local models frequently report nothing. Estimate so the record is still
            // usable, and flag it so nobody mistakes the number for a measurement.
            isEstimated = true;
            inputTokens = EstimateInputTokens(context.Messages);
            outputTokens = _estimator.EstimateTokens(context.CompletionText);
        }
        else
        {
            inputTokens = reportedInput ?? 0;
            outputTokens = reportedOutput ?? 0;
        }

        var cachedInput = ReadAdditionalCount(context.Usage, CachedInputKeys);
        var cacheWrite = ReadAdditionalCount(context.Usage, CacheWriteKeys);

        var cost = _costCalculator.Calculate(modelId, inputTokens, outputTokens, cachedInput, cacheWrite);

        var attribution = _attribution.Current;

        return new UsageRecord
        {
            TimestampUtc = context.StartedAt,
            ModelId = modelId,
            ProviderName = ProviderName(),
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = context.Usage?.TotalTokenCount ?? (inputTokens + outputTokens),
            CachedInputTokens = cachedInput,
            CacheWriteTokens = cacheWrite,
            Cost = cost.Cost,
            Currency = cost.Currency,
            IsEstimated = isEstimated,
            PricingFound = cost.PriceFound,
            DurationMs = context.Elapsed.TotalMilliseconds,
            Success = context.Exception is null,
            ErrorType = context.Exception?.GetType().Name,
            ErrorMessage = context.Exception?.Message,
            Streamed = context.Streamed,
            TenantId = attribution?.TenantId ?? _options.DefaultTenantId,
            Feature = attribution?.Feature ?? _options.DefaultFeature,
            UserId = attribution?.UserId,
            ResponseId = context.ResponseId,
            Prompt = _options.RecordPromptContent ? Truncate(FlattenMessages(context.Messages)) : null,
            Completion = _options.RecordPromptContent ? Truncate(context.CompletionText) : null,
        };
    }

    private string? MetadataModelId() =>
        (GetService(typeof(ChatClientMetadata)) as ChatClientMetadata)?.DefaultModelId;

    private string? ProviderName() =>
        (GetService(typeof(ChatClientMetadata)) as ChatClientMetadata)?.ProviderName;

    private long EstimateInputTokens(IEnumerable<ChatMessage>? messages)
    {
        if (messages is null)
        {
            return 0;
        }

        long total = 0;
        foreach (var message in messages)
        {
            total += _estimator.EstimateTokens(message.Text);
        }

        return total;
    }

    private static string? FlattenMessages(IEnumerable<ChatMessage>? messages)
    {
        if (messages is null)
        {
            return null;
        }

        return string.Join("\n", messages.Select(m => $"{m.Role}: {m.Text}"));
    }

    private string? Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var max = _options.MaxRecordedContentLength;
        return text.Length <= max ? text : text[..max];
    }

    private static long? ReadAdditionalCount(UsageDetails? usage, string[] keys)
    {
        var counts = usage?.AdditionalCounts;
        if (counts is null || counts.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            foreach (var pair in counts)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value;
                }
            }
        }

        return null;
    }

    private static UsageDetails Merge(UsageDetails? existing, UsageDetails incoming)
    {
        if (existing is null)
        {
            return incoming;
        }

        // Some providers emit usage more than once across a stream (a partial early,
        // a final at the end). Prefer the later non-null value for each field.
        existing.InputTokenCount = incoming.InputTokenCount ?? existing.InputTokenCount;
        existing.OutputTokenCount = incoming.OutputTokenCount ?? existing.OutputTokenCount;
        existing.TotalTokenCount = incoming.TotalTokenCount ?? existing.TotalTokenCount;

        if (incoming.AdditionalCounts is { Count: > 0 } incomingCounts)
        {
            existing.AdditionalCounts ??= [];
            foreach (var pair in incomingCounts)
            {
                existing.AdditionalCounts[pair.Key] = pair.Value;
            }
        }

        return existing;
    }

    private sealed class RecordContext
    {
        public DateTimeOffset StartedAt { get; init; }
        public TimeSpan Elapsed { get; init; }
        public string? RequestedModelId { get; init; }
        public IEnumerable<ChatMessage>? Messages { get; init; }
        public bool Streamed { get; init; }
        public UsageDetails? Usage { get; init; }
        public string? ResponseModelId { get; init; }
        public string? ResponseId { get; init; }
        public string? CompletionText { get; init; }
        public Exception? Exception { get; init; }
    }
}
