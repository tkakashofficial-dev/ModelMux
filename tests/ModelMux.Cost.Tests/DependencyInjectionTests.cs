using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelMux.Cost.Attribution;
using ModelMux.Cost.Estimation;
using ModelMux.Cost.Pricing;

namespace ModelMux.Cost.Tests;

/// <summary>
/// Exercises the real DI container end to end. Unit tests that construct services by hand
/// cannot catch registration faults such as ambiguous constructors, so these build the
/// container the way an application actually does.
/// </summary>
public class DependencyInjectionTests
{
    private static ServiceProvider BuildProvider(Action<CostTrackingOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddCostTracking(configure ?? (_ => { }));
        services.AddChatClient(FakeChatClient.WithUsage(1_000, 500)).UseCostTracking();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Container_resolves_every_registered_service()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<IPricingResolver>());
        Assert.NotNull(provider.GetRequiredService<ICostCalculator>());
        Assert.NotNull(provider.GetRequiredService<ITokenEstimator>());
        Assert.NotNull(provider.GetRequiredService<IUsageAttributionAccessor>());
        Assert.NotNull(provider.GetRequiredService<IUsageStore>());
        Assert.NotNull(provider.GetRequiredService<IUsageQuery>());
        Assert.NotNull(provider.GetRequiredService<IChatClient>());
    }

    [Fact]
    public void Resolving_the_chat_client_wraps_it_in_the_middleware()
    {
        using var provider = BuildProvider();

        var client = provider.GetRequiredService<IChatClient>();

        Assert.IsType<CostTrackingChatClient>(client);
    }

    [Fact]
    public async Task End_to_end_through_the_container_records_usage()
    {
        using var provider = BuildProvider();

        var chat = provider.GetRequiredService<IChatClient>();
        var usage = provider.GetRequiredService<IUsageQuery>();

        using (UsageScope.Begin(tenantId: "acme", feature: "demo"))
        {
            await chat.GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        }

        var summary = await usage.SummarizeAsync(new UsageFilter { TenantId = "acme" });
        Assert.Equal(1, summary.RequestCount);
        Assert.Equal(1_500, summary.TotalTokens);
        Assert.Equal(0.0175m, summary.Cost);
    }

    [Fact]
    public void Store_and_query_resolve_to_the_same_instance()
    {
        // Otherwise writes land in one store and reads come from an empty one.
        using var provider = BuildProvider();

        Assert.Same(
            provider.GetRequiredService<IUsageStore>(),
            provider.GetRequiredService<IUsageQuery>());
    }

    [Fact]
    public void An_application_supplied_store_is_not_replaced()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUsageStore, ThrowingUsageStore>();
        services.AddCostTracking();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<ThrowingUsageStore>(provider.GetRequiredService<IUsageStore>());
    }

    [Fact]
    public void UseCostTracking_without_AddCostTracking_fails_with_an_actionable_message()
    {
        var services = new ServiceCollection();
        services.AddChatClient(FakeChatClient.WithUsage(1, 1)).UseCostTracking();

        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IChatClient>());

        Assert.Contains("AddCostTracking", ex.Message, StringComparison.Ordinal);
    }
}
