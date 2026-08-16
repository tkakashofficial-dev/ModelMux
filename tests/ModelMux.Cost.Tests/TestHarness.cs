using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelMux.Cost.Attribution;
using ModelMux.Cost.Estimation;
using ModelMux.Cost.Pricing;
using ModelMux.Cost.Stores;

namespace ModelMux.Cost.Tests;

/// <summary>Builds a <see cref="CostTrackingChatClient"/> wired to an in-memory store for assertions.</summary>
internal sealed class TestHarness
{
    public required InMemoryUsageStore Store { get; init; }
    public required CostTrackingChatClient Client { get; init; }
    public required FakeChatClient Inner { get; init; }

    public static TestHarness Create(
        FakeChatClient inner,
        Action<CostTrackingOptions>? configure = null,
        IUsageStore? storeOverride = null)
    {
        var options = new CostTrackingOptions();
        configure?.Invoke(options);

        var wrapped = Options.Create(options);
        var store = new InMemoryUsageStore();
        var resolver = new PricingResolver(wrapped);

        var client = new CostTrackingChatClient(
            inner,
            storeOverride ?? store,
            new CostCalculator(resolver),
            new HeuristicTokenEstimator(),
            new AmbientUsageAttributionAccessor(),
            wrapped);

        return new TestHarness { Store = store, Client = client, Inner = inner };
    }

    public async Task<UsageRecord> SingleRecordAsync()
    {
        var records = await Store.QueryAsync(new UsageFilter { Limit = 100 });
        return Assert.Single(records);
    }

    public static List<ChatMessage> Prompt(string text = "What is the capital of France?") =>
        [new ChatMessage(ChatRole.User, text)];
}
