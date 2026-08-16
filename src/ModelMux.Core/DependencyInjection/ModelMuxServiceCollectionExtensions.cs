using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelMux.Providers;

namespace ModelMux;

/// <summary>Registers ModelMux into an application's service collection.</summary>
public static class ModelMuxServiceCollectionExtensions
{
    /// <summary>
    /// Registers ModelMux and binds options from configuration (section <c>ModelMux</c> by
    /// default), so providers and models can change without touching code.
    /// </summary>
    /// <remarks>
    /// Also registers <see cref="IChatClient"/> pointing at the default profile, so existing
    /// code that injects <see cref="IChatClient"/> keeps working with no change.
    /// </remarks>
    public static ModelMuxBuilder AddModelMux(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = ModelMuxOptions.SectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ModelMuxOptions>().Bind(configuration.GetSection(sectionName));

        return AddCore(services);
    }

    /// <summary>Registers ModelMux with options configured in code.</summary>
    public static ModelMuxBuilder AddModelMux(
        this IServiceCollection services,
        Action<ModelMuxOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        return AddCore(services);
    }

    private static ModelMuxBuilder AddCore(IServiceCollection services)
    {
        foreach (var provider in KnownProviders.CreateDefaults())
        {
            // Enumerable registration: a custom provider added later under the same name
            // wins, because the router keeps the last one registered for each name.
            services.AddSingleton(provider);
        }

        // Registered first, so it ends up the innermost decorator and sees raw provider
        // exceptions before anything else has a chance to wrap them.
        services.AddSingleton<IChatClientDecorator, Errors.ErrorMappingDecorator>();

        services.TryAddSingleton<ModelMuxRouter>();
        services.TryAddSingleton<IModelMux>(sp => sp.GetRequiredService<ModelMuxRouter>());

        // Registering IChatClient means application code never has to know ModelMux exists.
        services.TryAddSingleton<IChatClient>(sp => sp.GetRequiredService<IModelMux>().GetClient());

        return new ModelMuxBuilder(services);
    }
}

/// <summary>Fluent surface for extending a ModelMux registration.</summary>
/// <param name="services">The service collection being configured.</param>
public sealed class ModelMuxBuilder(IServiceCollection services)
{
    /// <summary>The underlying service collection.</summary>
    public IServiceCollection Services { get; } = services;

    /// <summary>
    /// Registers a provider, replacing any built-in provider with the same
    /// <see cref="IChatProvider.Name"/>.
    /// </summary>
    public ModelMuxBuilder AddProvider(IChatProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Services.AddSingleton(provider);
        return this;
    }

    /// <summary>
    /// Registers a provider resolved from the container, so it can take its own dependencies.
    /// </summary>
    public ModelMuxBuilder AddProvider<TProvider>()
        where TProvider : class, IChatProvider
    {
        Services.AddSingleton<IChatProvider, TProvider>();
        return this;
    }

    /// <summary>
    /// Registers an endpoint that speaks the OpenAI protocol — vLLM, LM Studio, LocalAI, a
    /// rented GPU, or any gateway — as a named provider.
    /// </summary>
    /// <param name="name">Provider name to use in profiles.</param>
    /// <param name="endpoint">Base URL of the endpoint.</param>
    /// <param name="requiresApiKey">False when the endpoint ignores credentials.</param>
    public ModelMuxBuilder AddOpenAICompatibleProvider(
        string name,
        Uri endpoint,
        bool requiresApiKey = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(endpoint);

        return AddProvider(new OpenAICompatibleProvider(name, endpoint, requiresApiKey));
    }
}
