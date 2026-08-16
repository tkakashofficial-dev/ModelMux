using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelMux.Cost.Attribution;
using ModelMux.Cost.Estimation;
using ModelMux.Cost.Pricing;

namespace ModelMux.Cost;

/// <summary>
/// Connects cost tracking to a ModelMux registration, so every profile is measured without
/// application code changing.
/// </summary>
public static class ModelMuxCostIntegration
{
    /// <summary>
    /// Records tokens, cost, latency, and attribution for every call ModelMux routes.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services
    ///     .AddModelMux(builder.Configuration)
    ///     .AddCostTracking();
    /// </code>
    /// </example>
    public static ModelMuxBuilder AddCostTracking(
        this ModelMuxBuilder builder,
        Action<CostTrackingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddCostTracking(configure ?? (_ => { }));

        // Registered last so it ends up the outermost decorator and its recorded duration
        // covers everything else in the pipeline.
        builder.Services.AddSingleton<IChatClientDecorator, CostTrackingDecorator>();

        return builder;
    }
}

/// <summary>
/// Wraps each routed client in a <see cref="CostTrackingChatClient"/>, tagging usage with the
/// profile that served it.
/// </summary>
internal sealed class CostTrackingDecorator(
    IUsageStore store,
    ICostCalculator costCalculator,
    ITokenEstimator estimator,
    IUsageAttributionAccessor attribution,
    IOptions<CostTrackingOptions> options,
    TimeProvider? timeProvider = null,
    ILogger<CostTrackingChatClient>? logger = null) : IChatClientDecorator
{
    public IChatClient Decorate(string profileName, ModelProfile profile, IChatClient client) =>
        new CostTrackingChatClient(
            client,
            store,
            costCalculator,
            estimator,
            // The profile name is the most useful attribution ModelMux can supply on its own,
            // so it fills in Feature when the application hasn't opened a scope of its own.
            new ProfileAwareAttributionAccessor(attribution, profileName),
            options,
            timeProvider,
            logger);
}

/// <summary>
/// Falls back to the profile name for <see cref="UsageAttribution.Feature"/> when the
/// application hasn't set one, so per-profile cost is available with zero caller effort.
/// </summary>
internal sealed class ProfileAwareAttributionAccessor(
    IUsageAttributionAccessor inner,
    string profileName) : IUsageAttributionAccessor
{
    public UsageAttribution? Current
    {
        get
        {
            var current = inner.Current;

            return new UsageAttribution
            {
                TenantId = current?.TenantId,
                Feature = current?.Feature ?? profileName,
                UserId = current?.UserId,
            };
        }
    }
}
