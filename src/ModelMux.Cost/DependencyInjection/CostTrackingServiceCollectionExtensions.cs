using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelMux.Cost.Attribution;
using ModelMux.Cost.Estimation;
using ModelMux.Cost.Pricing;
using ModelMux.Cost.Stores;

namespace ModelMux.Cost;

/// <summary>Registers ModelMux.Cost services.</summary>
public static class CostTrackingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ModelMux.Cost services, defaulting to an in-memory usage store.
    /// Call <c>UseCostTracking()</c> on your <see cref="ChatClientBuilder"/> to activate tracking.
    /// </summary>
    public static IServiceCollection AddCostTracking(
        this IServiceCollection services,
        Action<CostTrackingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<CostTrackingOptions>();
        }

        // TryAdd throughout, so an application that registered its own store, estimator,
        // or attribution accessor before calling AddCostTracking keeps it.
        services.TryAddSingleton<InMemoryUsageStore>();
        services.TryAddSingleton<IUsageStore>(sp => sp.GetRequiredService<InMemoryUsageStore>());
        services.TryAddSingleton<IUsageQuery>(sp => sp.GetRequiredService<InMemoryUsageStore>());

        // Registered via an explicit factory: PricingResolver has two public constructors
        // and the container cannot pick between them on its own.
        services.TryAddSingleton<IPricingResolver>(sp =>
            new PricingResolver(sp.GetRequiredService<IOptions<CostTrackingOptions>>()));
        services.TryAddSingleton<ICostCalculator, CostCalculator>();
        services.TryAddSingleton<ITokenEstimator, HeuristicTokenEstimator>();
        services.TryAddSingleton<IUsageAttributionAccessor, AmbientUsageAttributionAccessor>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Registers ModelMux.Cost and binds options from configuration (section <c>ModelMux.Cost</c>
    /// by default), so pricing can be updated without a redeploy.
    /// </summary>
    public static IServiceCollection AddCostTracking(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = CostTrackingOptions.SectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CostTrackingOptions>().Bind(configuration.GetSection(sectionName));

        return services.AddCostTracking(configure: null);
    }
}

/// <summary>Adds ModelMux.Cost to an <see cref="IChatClient"/> pipeline.</summary>
public static class CostTrackingChatClientBuilderExtensions
{
    /// <summary>
    /// Records token usage, cost, latency, and attribution for every call through this
    /// pipeline.
    /// </summary>
    /// <remarks>
    /// Place it as the outermost middleware you care about measuring â€” anything registered
    /// after it in the builder chain runs inside it and is included in the recorded duration.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddCostTracking();
    /// builder.Services
    ///     .AddChatClient(innerClient)
    ///     .UseCostTracking();
    /// </code>
    /// </example>
    public static ChatClientBuilder UseCostTracking(
        this ChatClientBuilder builder,
        Action<CostTrackingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.Use((innerClient, services) =>
        {
            var options = services.GetService<IOptions<CostTrackingOptions>>()
                ?? throw new InvalidOperationException(
                    $"ModelMux.Cost services are not registered. Call {nameof(CostTrackingServiceCollectionExtensions.AddCostTracking)}() "
                    + "on your service collection before calling UseCostTracking().");

            if (configure is not null)
            {
                configure(options.Value);
            }

            return new CostTrackingChatClient(
                innerClient,
                services.GetRequiredService<IUsageStore>(),
                services.GetRequiredService<ICostCalculator>(),
                services.GetRequiredService<ITokenEstimator>(),
                services.GetRequiredService<IUsageAttributionAccessor>(),
                options,
                services.GetService<TimeProvider>(),
                services.GetService<ILogger<CostTrackingChatClient>>());
        });
    }
}
