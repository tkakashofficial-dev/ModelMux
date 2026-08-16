using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelMux.Cost.Attribution;

namespace ModelMux.Cost.Tests;

/// <summary>
/// Proves the two packages compose: routing decides which provider serves a call, cost
/// tracking records what it cost. Neither package knows the other's internals.
/// </summary>
public class ModelMuxIntegrationTests
{
    private sealed class StubProvider(string name) : IChatProvider
    {
        public string Name { get; } = name;

        public IChatClient CreateClient(string profileName, ModelProfile profile) =>
            new StubClient(profile.Model);
    }

    private sealed class StubClient(string model) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"))
            {
                ModelId = model,
                Usage = new UsageDetails
                {
                    InputTokenCount = 1_000,
                    OutputTokenCount = 500,
                    TotalTokenCount = 1_500,
                },
            });

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static ServiceProvider BuildApp()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = "fast",
            ["ModelMux:Profiles:fast:Provider"] = "Gemini",
            ["ModelMux:Profiles:fast:Model"] = "gemini-2.5-flash",
            ["ModelMux:Profiles:smart:Provider"] = "OpenAI",
            ["ModelMux:Profiles:smart:Model"] = "gpt-5",
        }).Build();

        var services = new ServiceCollection();
        services.AddModelMux(config)
            .AddProvider(new StubProvider("Gemini"))
            .AddProvider(new StubProvider("OpenAI"))
            .AddCostTracking();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Routed_calls_are_costed_with_the_price_of_the_model_that_served_them()
    {
        using var app = BuildApp();
        var mux = app.GetRequiredService<IModelMux>();
        var usage = app.GetRequiredService<IUsageQuery>();

        await mux.GetClient("fast").GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        await mux.GetClient("smart").GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var records = await usage.QueryAsync(new UsageFilter());
        Assert.Equal(2, records.Count);

        // gemini-2.5-flash: 1000 in @ $0.30/M + 500 out @ $2.50/M = $0.0003 + $0.00125
        var gemini = records.Single(r => r.ModelId == "gemini-2.5-flash");
        Assert.Equal(0.00155m, gemini.Cost);

        // gpt-5: 1000 in @ $1.25/M + 500 out @ $10.00/M = $0.00125 + $0.005
        var openAi = records.Single(r => r.ModelId == "gpt-5");
        Assert.Equal(0.00625m, openAi.Cost);
    }

    [Fact]
    public async Task Cost_is_attributed_to_the_profile_when_the_app_sets_no_feature()
    {
        // Per-profile cost should be available without callers doing anything.
        using var app = BuildApp();
        var mux = app.GetRequiredService<IModelMux>();
        var usage = app.GetRequiredService<IUsageQuery>();

        await mux.GetClient("smart").GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        var summary = await usage.SummarizeAsync(new UsageFilter { Feature = "smart" });
        Assert.Equal(1, summary.RequestCount);
    }

    [Fact]
    public async Task An_application_scope_still_wins_over_the_profile_name()
    {
        using var app = BuildApp();
        var mux = app.GetRequiredService<IModelMux>();
        var usage = app.GetRequiredService<IUsageQuery>();

        using (UsageScope.Begin(tenantId: "acme", feature: "invoice-extraction"))
        {
            await mux.GetClient("fast").GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);
        }

        var byFeature = await usage.SummarizeAsync(new UsageFilter { Feature = "invoice-extraction" });
        var byTenant = await usage.SummarizeAsync(new UsageFilter { TenantId = "acme" });

        Assert.Equal(1, byFeature.RequestCount);
        Assert.Equal(1, byTenant.RequestCount);
    }

    [Fact]
    public async Task Cost_tracking_does_not_change_the_response()
    {
        using var app = BuildApp();

        var response = await app.GetRequiredService<IChatClient>()
            .GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]);

        Assert.Equal("ok", response.Text);
    }

    [Fact]
    public void Routing_works_without_the_cost_package_registered()
    {
        // ModelMux.Cost must stay optional; Core cannot depend on it.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ModelMux:Profiles:fast:Provider"] = "Gemini",
            ["ModelMux:Profiles:fast:Model"] = "gemini-2.5-flash",
        }).Build();

        var services = new ServiceCollection();
        services.AddModelMux(config).AddProvider(new StubProvider("Gemini"));

        using var app = services.BuildServiceProvider();

        Assert.NotNull(app.GetRequiredService<IChatClient>());
        Assert.Null(app.GetService<IUsageQuery>());
    }
}
