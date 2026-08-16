using Microsoft.Extensions.AI;
using ModelMux.Cost.Attribution;

namespace ModelMux.Cost.Tests;

public class CostTrackingChatClientTests
{
    [Fact]
    public async Task Records_tokens_cost_and_model_for_a_successful_call()
    {
        var harness = TestHarness.Create(FakeChatClient.WithUsage(1_000, 500));

        await harness.Client.GetResponseAsync(TestHarness.Prompt());

        var record = await harness.SingleRecordAsync();
        Assert.Equal("claude-opus-5", record.ModelId);
        Assert.Equal(1_000, record.InputTokens);
        Assert.Equal(500, record.OutputTokens);
        Assert.Equal(1_500, record.TotalTokens);
        Assert.Equal(0.0175m, record.Cost);
        Assert.Equal("USD", record.Currency);
        Assert.True(record.PricingFound);
        Assert.False(record.IsEstimated);
        Assert.True(record.Success);
        Assert.False(record.Streamed);
        Assert.Equal("fake-provider", record.ProviderName);
        Assert.Equal("resp-1", record.ResponseId);
        Assert.True(record.DurationMs >= 0);
    }

    [Fact]
    public async Task Passes_the_response_through_unchanged()
    {
        var harness = TestHarness.Create(FakeChatClient.WithUsage(10, 10, responseText: "Paris"));

        var response = await harness.Client.GetResponseAsync(TestHarness.Prompt());

        Assert.Equal("Paris", response.Text);
        Assert.Equal(1, harness.Inner.CallCount);
    }

    [Fact]
    public async Task Records_the_failure_and_rethrows()
    {
        var harness = TestHarness.Create(FakeChatClient.ThatThrows(new TimeoutException("upstream timed out")));

        await Assert.ThrowsAsync<TimeoutException>(
            () => harness.Client.GetResponseAsync(TestHarness.Prompt()));

        var record = await harness.SingleRecordAsync();
        Assert.False(record.Success);
        Assert.Equal(nameof(TimeoutException), record.ErrorType);
        Assert.Equal("upstream timed out", record.ErrorMessage);
    }

    [Fact]
    public async Task Marks_records_as_estimated_when_the_provider_reports_no_usage()
    {
        var harness = TestHarness.Create(FakeChatClient.WithoutUsage());

        await harness.Client.GetResponseAsync(TestHarness.Prompt());

        var record = await harness.SingleRecordAsync();
        Assert.True(record.IsEstimated);
        Assert.True(record.InputTokens > 0);
        Assert.True(record.OutputTokens > 0);
    }

    [Fact]
    public async Task Does_not_estimate_when_estimation_is_disabled()
    {
        var harness = TestHarness.Create(
            FakeChatClient.WithoutUsage(),
            o => o.EstimateTokensWhenMissing = false);

        await harness.Client.GetResponseAsync(TestHarness.Prompt());

        var record = await harness.SingleRecordAsync();
        Assert.False(record.IsEstimated);
        Assert.Equal(0, record.InputTokens);
        Assert.Equal(0, record.OutputTokens);
    }

    [Fact]
    public async Task Leaves_cost_null_for_an_unpriced_model_rather_than_reporting_zero()
    {
        var harness = TestHarness.Create(FakeChatClient.WithUsage(100, 100, modelId: "unpriced-model"));

        await harness.Client.GetResponseAsync(TestHarness.Prompt());

        var record = await harness.SingleRecordAsync();
        Assert.Null(record.Cost);
        Assert.False(record.PricingFound);
    }

    [Fact]
    public async Task Reads_cache_token_counts_from_provider_reported_additional_counts()
    {
        var harness = TestHarness.Create(FakeChatClient.WithUsage(
            inputTokens: 1_000,
            outputTokens: 0,
            additionalCounts: new Dictionary<string, long>
            {
                ["cache_read_input_tokens"] = 800,
                ["cache_creation_input_tokens"] = 100,
            }));

        await harness.Client.GetResponseAsync(TestHarness.Prompt());

        var record = await harness.SingleRecordAsync();
        Assert.Equal(800, record.CachedInputTokens);
        Assert.Equal(100, record.CacheWriteTokens);
    }

    [Fact]
    public async Task Does_not_record_prompt_content_by_default()
    {
        // Prompts routinely contain personal data; storing them must be a deliberate choice.
        var harness = TestHarness.Create(FakeChatClient.WithUsage(10, 10, responseText: "secret answer"));

        await harness.Client.GetResponseAsync(TestHarness.Prompt("my national id is 12345"));

        var record = await harness.SingleRecordAsync();
        Assert.Null(record.Prompt);
        Assert.Null(record.Completion);
    }

    [Fact]
    public async Task Records_prompt_content_when_explicitly_enabled_and_truncates_it()
    {
        var harness = TestHarness.Create(
            FakeChatClient.WithUsage(10, 10, responseText: new string('b', 500)),
            o =>
            {
                o.RecordPromptContent = true;
                o.MaxRecordedContentLength = 50;
            });

        await harness.Client.GetResponseAsync(TestHarness.Prompt(new string('a', 500)));

        var record = await harness.SingleRecordAsync();
        Assert.NotNull(record.Prompt);
        Assert.Equal(50, record.Prompt.Length);
        Assert.NotNull(record.Completion);
        Assert.Equal(50, record.Completion.Length);
    }

    [Fact]
    public async Task Records_nothing_when_disabled_but_still_serves_the_call()
    {
        var harness = TestHarness.Create(
            FakeChatClient.WithUsage(10, 10, responseText: "still works"),
            o => o.Enabled = false);

        var response = await harness.Client.GetResponseAsync(TestHarness.Prompt());

        Assert.Equal("still works", response.Text);
        var records = await harness.Store.QueryAsync(new UsageFilter());
        Assert.Empty(records);
    }

    [Fact]
    public async Task A_failing_usage_store_does_not_break_the_llm_call()
    {
        // Telemetry is never allowed to take down the feature it measures.
        var harness = TestHarness.Create(
            FakeChatClient.WithUsage(10, 10, responseText: "unaffected"),
            storeOverride: new ThrowingUsageStore());

        var response = await harness.Client.GetResponseAsync(TestHarness.Prompt());

        Assert.Equal("unaffected", response.Text);
    }

    [Fact]
    public async Task Applies_default_tenant_and_feature_when_no_scope_is_open()
    {
        var harness = TestHarness.Create(
            FakeChatClient.WithUsage(10, 10),
            o =>
            {
                o.DefaultTenantId = "default-tenant";
                o.DefaultFeature = "default-feature";
            });

        await harness.Client.GetResponseAsync(TestHarness.Prompt());

        var record = await harness.SingleRecordAsync();
        Assert.Equal("default-tenant", record.TenantId);
        Assert.Equal("default-feature", record.Feature);
    }
}

public class StreamingTests
{
    private static ChatResponseUpdate TextUpdate(string text, string? modelId = null) =>
        new(ChatRole.Assistant, text) { ModelId = modelId };

    private static ChatResponseUpdate UsageUpdate(long input, long output) =>
        new()
        {
            Contents = [new UsageContent(new UsageDetails
            {
                InputTokenCount = input,
                OutputTokenCount = output,
                TotalTokenCount = input + output,
            })],
        };

    [Fact]
    public async Task Records_usage_that_arrives_at_the_end_of_the_stream()
    {
        // Usage lands in a trailing update, not the first one, so it has to be accumulated.
        var harness = TestHarness.Create(FakeChatClient.Streaming(
            TextUpdate("Par", "claude-opus-5"),
            TextUpdate("is"),
            UsageUpdate(1_000, 500)));

        var text = "";
        await foreach (var update in harness.Client.GetStreamingResponseAsync(TestHarness.Prompt()))
        {
            text += update.Text;
        }

        Assert.Equal("Paris", text);

        var record = await harness.SingleRecordAsync();
        Assert.True(record.Streamed);
        Assert.Equal(1_000, record.InputTokens);
        Assert.Equal(500, record.OutputTokens);
        Assert.Equal(0.0175m, record.Cost);
        Assert.True(record.Success);
    }

    [Fact]
    public async Task Writes_exactly_one_record_per_stream()
    {
        var harness = TestHarness.Create(FakeChatClient.Streaming(
            TextUpdate("a", "claude-opus-5"),
            TextUpdate("b"),
            TextUpdate("c"),
            UsageUpdate(10, 10)));

        await foreach (var _ in harness.Client.GetStreamingResponseAsync(TestHarness.Prompt()))
        {
        }

        var records = await harness.Store.QueryAsync(new UsageFilter());
        Assert.Single(records);
    }

    [Fact]
    public async Task Records_a_stream_that_fails_midway_and_rethrows()
    {
        var harness = TestHarness.Create(FakeChatClient.StreamingThatThrows(
            TextUpdate("partial", "claude-opus-5"),
            new HttpRequestException("connection reset")));

        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in harness.Client.GetStreamingResponseAsync(TestHarness.Prompt()))
            {
            }
        });

        var record = await harness.SingleRecordAsync();
        Assert.True(record.Streamed);
        Assert.False(record.Success);
        Assert.Equal(nameof(HttpRequestException), record.ErrorType);
    }

    [Fact]
    public async Task Records_usage_even_when_the_consumer_abandons_the_stream_early()
    {
        // Tokens already generated were billed whether or not the caller read them.
        var harness = TestHarness.Create(FakeChatClient.Streaming(
            TextUpdate("one", "claude-opus-5"),
            TextUpdate("two"),
            UsageUpdate(10, 10)));

        await foreach (var _ in harness.Client.GetStreamingResponseAsync(TestHarness.Prompt()))
        {
            break;
        }

        var records = await harness.Store.QueryAsync(new UsageFilter());
        Assert.Single(records);
    }
}

public class AttributionTests
{
    [Fact]
    public async Task Ambient_scope_flows_into_the_record()
    {
        var harness = TestHarness.Create(FakeChatClient.WithUsage(10, 10));

        using (UsageScope.Begin(tenantId: "acme", feature: "invoice-extraction", userId: "u-1"))
        {
            await harness.Client.GetResponseAsync(TestHarness.Prompt());
        }

        var record = await harness.SingleRecordAsync();
        Assert.Equal("acme", record.TenantId);
        Assert.Equal("invoice-extraction", record.Feature);
        Assert.Equal("u-1", record.UserId);
    }

    [Fact]
    public async Task Scope_flows_across_await_boundaries()
    {
        var harness = TestHarness.Create(FakeChatClient.WithUsage(10, 10));

        using (UsageScope.Begin(tenantId: "acme"))
        {
            await Task.Yield();
            await Task.Run(() => harness.Client.GetResponseAsync(TestHarness.Prompt()));
        }

        var record = await harness.SingleRecordAsync();
        Assert.Equal("acme", record.TenantId);
    }

    [Fact]
    public void Nested_scope_inherits_unspecified_values_from_the_outer_scope()
    {
        using (UsageScope.Begin(tenantId: "acme", feature: "outer"))
        {
            using (UsageScope.Begin(feature: "inner"))
            {
                Assert.Equal("acme", UsageScope.Current?.TenantId);
                Assert.Equal("inner", UsageScope.Current?.Feature);
            }

            Assert.Equal("outer", UsageScope.Current?.Feature);
        }

        Assert.Null(UsageScope.Current);
    }

    [Fact]
    public async Task Records_from_different_tenants_are_separable()
    {
        var harness = TestHarness.Create(FakeChatClient.WithUsage(1_000, 1_000));

        using (UsageScope.Begin(tenantId: "tenant-a"))
        {
            await harness.Client.GetResponseAsync(TestHarness.Prompt());
        }

        using (UsageScope.Begin(tenantId: "tenant-b"))
        {
            await harness.Client.GetResponseAsync(TestHarness.Prompt());
            await harness.Client.GetResponseAsync(TestHarness.Prompt());
        }

        var a = await harness.Store.SummarizeAsync(new UsageFilter { TenantId = "tenant-a" });
        var b = await harness.Store.SummarizeAsync(new UsageFilter { TenantId = "tenant-b" });

        Assert.Equal(1, a.RequestCount);
        Assert.Equal(2, b.RequestCount);
        Assert.Equal(b.Cost, a.Cost * 2);
    }
}
