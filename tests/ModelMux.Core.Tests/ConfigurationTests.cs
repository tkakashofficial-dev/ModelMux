using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelMux.Providers;

namespace ModelMux.Core.Tests;

/// <summary>
/// Misconfiguration is the most common way a library like this wastes someone's afternoon,
/// so every failure here has to name the offending key and say what to do about it.
/// </summary>
public class ConfigurationTests
{
    private static ModelMuxRouter Router(ModelMuxOptions options, params IChatProvider[] providers) =>
        new(Options.Create(options), providers.Length == 0 ? [new FakeProvider("Gemini")] : providers);

    private static ModelMuxOptions WithProfiles(params (string Name, string Provider, string Model)[] profiles)
    {
        var options = new ModelMuxOptions();
        foreach (var (name, provider, model) in profiles)
        {
            options.Profiles[name] = new ModelProfile { Provider = provider, Model = model };
        }

        return options;
    }

    [Fact]
    public void A_single_profile_becomes_the_default_without_being_named()
    {
        var router = Router(WithProfiles(("only", "Gemini", "gemini-2.5-flash")));

        Assert.Equal("only", router.DefaultProfileName);
    }

    [Fact]
    public void No_profiles_at_all_fails_with_a_message_naming_the_config_section()
    {
        var ex = Assert.Throws<ModelMuxConfigurationException>(() => Router(new ModelMuxOptions()));

        Assert.Contains("ModelMux:Profiles", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_profiles_without_a_default_fails_and_lists_the_choices()
    {
        var options = WithProfiles(("fast", "Gemini", "a"), ("smart", "Gemini", "b"));

        var ex = Assert.Throws<ModelMuxConfigurationException>(() => Router(options));

        Assert.Contains("DefaultProfile", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fast", ex.Message, StringComparison.Ordinal);
        Assert.Contains("smart", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_default_naming_a_missing_profile_fails_at_startup_not_at_first_request()
    {
        var options = WithProfiles(("fast", "Gemini", "a"));
        options.DefaultProfile = "typo";

        var ex = Assert.Throws<ModelMuxConfigurationException>(() => Router(options));

        Assert.Contains("typo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unregistered_provider_fails_and_lists_the_registered_ones()
    {
        var router = Router(WithProfiles(("fast", "Groq", "mixtral")));

        var ex = Assert.Throws<ModelMuxConfigurationException>(() => router.GetClient());

        Assert.Contains("Groq", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Gemini", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Requesting_an_unknown_profile_lists_the_available_ones()
    {
        var router = Router(WithProfiles(("fast", "Gemini", "a")));

        var ex = Assert.Throws<ModelMuxConfigurationException>(() => router.GetClient("nope"));

        Assert.Contains("nope", ex.Message, StringComparison.Ordinal);
        Assert.Contains("fast", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_with_no_provider_names_the_key_to_fix()
    {
        var options = new ModelMuxOptions();
        options.Profiles["fast"] = new ModelProfile { Model = "x" };

        var router = Router(options);
        var ex = Assert.Throws<ModelMuxConfigurationException>(() => router.GetClient());

        Assert.Contains("ModelMux:Profiles:fast:Provider", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Clients_are_created_once_per_profile_and_reused()
    {
        // Provider clients own HTTP connection pools; rebuilding one per request exhausts sockets.
        var provider = new FakeProvider("Gemini");
        var router = Router(WithProfiles(("fast", "Gemini", "a")), provider);

        var first = router.GetClient("fast");
        var second = router.GetClient("fast");

        Assert.Same(first, second);
        Assert.Equal(1, provider.CreateCount);
    }

    [Fact]
    public void Disposing_the_router_disposes_the_clients_it_created()
    {
        var router = Router(WithProfiles(("fast", "Gemini", "a")));
        var client = (FakeChatClient)router.GetClient();

        router.Dispose();

        Assert.True(client.IsDisposed);
    }

    [Fact]
    public void A_custom_provider_replaces_a_built_in_one_with_the_same_name()
    {
        // Lets an application swap in its own Gemini implementation without forking.
        var services = new ServiceCollection();
        services.AddModelMux(o =>
        {
            o.DefaultProfile = "fast";
            o.Profiles["fast"] = new ModelProfile { Provider = "Gemini", Model = "gemini-2.5-flash" };
        }).AddProvider(new FakeProvider("Gemini"));

        using var app = services.BuildServiceProvider();

        Assert.IsType<FakeChatClient>(app.GetRequiredService<IChatClient>());
    }
}

public class ApiKeyResolutionTests
{
    [Fact]
    public void The_environment_variable_is_preferred_over_a_literal_key()
    {
        // Config files end up in git; environment variables do not.
        const string variable = "MODELMUX_TEST_KEY";
        Environment.SetEnvironmentVariable(variable, "from-environment");

        try
        {
            var profile = new ModelProfile
            {
                ApiKey = "from-config",
                ApiKeyEnvironmentVariable = variable,
            };

            Assert.Equal("from-environment", profile.ResolveApiKey());
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void An_unset_environment_variable_falls_back_to_the_literal_key()
    {
        var profile = new ModelProfile
        {
            ApiKey = "from-config",
            ApiKeyEnvironmentVariable = "MODELMUX_DEFINITELY_NOT_SET",
        };

        Assert.Equal("from-config", profile.ResolveApiKey());
    }

    [Fact]
    public void No_key_anywhere_resolves_to_null()
    {
        Assert.Null(new ModelProfile().ResolveApiKey());
    }

    [Fact]
    public void A_provider_needing_a_key_explains_exactly_where_to_put_it()
    {
        var provider = new OpenAICompatibleProvider("OpenAI");
        var profile = new ModelProfile { Provider = "OpenAI", Model = "gpt-5" };

        var ex = Assert.Throws<ModelMuxConfigurationException>(
            () => provider.CreateClient("smart", profile));

        Assert.Contains("ApiKeyEnvironmentVariable", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Ollama_needs_no_key_because_a_local_server_ignores_it()
    {
        var provider = new OpenAICompatibleProvider(
            KnownProviders.Ollama,
            KnownProviders.OllamaEndpoint,
            requiresApiKey: false,
            unauthenticatedPlaceholder: "ollama");

        using var client = provider.CreateClient(
            "private",
            new ModelProfile { Provider = "Ollama", Model = "llama3" });

        Assert.NotNull(client);
    }

    [Fact]
    public void A_profile_with_no_model_says_so_before_any_network_call()
    {
        var provider = new OpenAICompatibleProvider("Ollama", requiresApiKey: false);

        var ex = Assert.Throws<ModelMuxConfigurationException>(
            () => provider.CreateClient("broken", new ModelProfile { Provider = "Ollama" }));

        Assert.Contains("Model", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_endpoint_is_rejected_with_the_offending_value()
    {
        var provider = new OpenAICompatibleProvider("Ollama", requiresApiKey: false);
        var profile = new ModelProfile { Model = "llama3", Endpoint = "not a url" };

        var ex = Assert.Throws<ModelMuxConfigurationException>(
            () => provider.CreateClient("broken", profile));

        Assert.Contains("not a url", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_built_in_provider_set_covers_openai_gemini_and_ollama()
    {
        var names = KnownProviders.CreateDefaults().Select(p => p.Name).ToList();

        Assert.Contains(KnownProviders.OpenAI, names);
        Assert.Contains(KnownProviders.Gemini, names);
        Assert.Contains(KnownProviders.Ollama, names);
    }
}
