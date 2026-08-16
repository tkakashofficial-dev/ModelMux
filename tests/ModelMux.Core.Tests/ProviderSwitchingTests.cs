using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ModelMux.Core.Tests;

/// <summary>
/// The core thesis: identical application code, different configuration, different provider.
/// If these ever fail, ModelMux has no reason to exist.
/// </summary>
public class ProviderSwitchingTests
{
    /// <summary>
    /// Stands in for real application code. It takes <see cref="IChatClient"/> and has no idea
    /// which provider is behind it — that is the property under test.
    /// </summary>
    private sealed class ApplicationService(IChatClient ai)
    {
        public async Task<string> RunAsync() =>
            (await ai.GetResponseAsync([new ChatMessage(ChatRole.User, "hello")])).Text;
    }

    private static ServiceProvider BuildApp(Dictionary<string, string?> configuration)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(configuration).Build();

        var services = new ServiceCollection();
        services.AddModelMux(config)
            .AddProvider(new FakeProvider("OpenAI"))
            .AddProvider(new FakeProvider("Gemini"))
            .AddProvider(new FakeProvider("Ollama"));
        services.AddSingleton<ApplicationService>();

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Gemini", "gemini-2.5-flash")]
    [InlineData("OpenAI", "gpt-5-mini")]
    [InlineData("Ollama", "llama3")]
    public async Task Same_application_code_serves_from_whichever_provider_config_names(
        string provider,
        string model)
    {
        // Only configuration differs between these cases. ApplicationService is byte-identical.
        using var app = BuildApp(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = "fast",
            ["ModelMux:Profiles:fast:Provider"] = provider,
            ["ModelMux:Profiles:fast:Model"] = model,
        });

        var result = await app.GetRequiredService<ApplicationService>().RunAsync();

        Assert.Equal($"served by {provider}/{model}", result);
    }

    [Fact]
    public async Task Switching_provider_requires_no_change_to_application_code()
    {
        static Dictionary<string, string?> Config(string provider, string model) => new()
        {
            ["ModelMux:DefaultProfile"] = "fast",
            ["ModelMux:Profiles:fast:Provider"] = provider,
            ["ModelMux:Profiles:fast:Model"] = model,
        };

        using var before = BuildApp(Config("Gemini", "gemini-2.5-flash"));
        using var after = BuildApp(Config("OpenAI", "gpt-5"));

        Assert.Equal(
            "served by Gemini/gemini-2.5-flash",
            await before.GetRequiredService<ApplicationService>().RunAsync());

        Assert.Equal(
            "served by OpenAI/gpt-5",
            await after.GetRequiredService<ApplicationService>().RunAsync());
    }

    [Fact]
    public void Profiles_let_one_app_use_several_providers_at_once()
    {
        using var app = BuildApp(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = "fast",
            ["ModelMux:Profiles:fast:Provider"] = "Gemini",
            ["ModelMux:Profiles:fast:Model"] = "gemini-2.5-flash",
            ["ModelMux:Profiles:smart:Provider"] = "OpenAI",
            ["ModelMux:Profiles:smart:Model"] = "gpt-5",
            ["ModelMux:Profiles:private:Provider"] = "Ollama",
            ["ModelMux:Profiles:private:Model"] = "llama3",
        });

        var mux = app.GetRequiredService<IModelMux>();

        Assert.Equal("Gemini", ((FakeChatClient)mux.GetClient("fast")).ProviderName);
        Assert.Equal("OpenAI", ((FakeChatClient)mux.GetClient("smart")).ProviderName);
        Assert.Equal("Ollama", ((FakeChatClient)mux.GetClient("private")).ProviderName);
    }

    [Fact]
    public void Plain_IChatClient_injection_resolves_to_the_default_profile()
    {
        // Existing code that already injects IChatClient must keep working untouched.
        using var app = BuildApp(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = "smart",
            ["ModelMux:Profiles:fast:Provider"] = "Gemini",
            ["ModelMux:Profiles:fast:Model"] = "gemini-2.5-flash",
            ["ModelMux:Profiles:smart:Provider"] = "OpenAI",
            ["ModelMux:Profiles:smart:Model"] = "gpt-5",
        });

        var client = (FakeChatClient)app.GetRequiredService<IChatClient>();

        Assert.Equal("OpenAI", client.ProviderName);
    }

    [Fact]
    public void Profile_names_are_case_insensitive()
    {
        using var app = BuildApp(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = "fast",
            ["ModelMux:Profiles:fast:Provider"] = "Gemini",
            ["ModelMux:Profiles:fast:Model"] = "gemini-2.5-flash",
        });

        var mux = app.GetRequiredService<IModelMux>();

        Assert.Same(mux.GetClient("fast"), mux.GetClient("FAST"));
    }

    [Fact]
    public void An_endpoint_override_reaches_the_provider_so_self_hosting_needs_no_code()
    {
        using var app = BuildApp(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = "gpu",
            ["ModelMux:Profiles:gpu:Provider"] = "OpenAI",
            ["ModelMux:Profiles:gpu:Model"] = "llama-3-70b",
            ["ModelMux:Profiles:gpu:Endpoint"] = "https://my-gpu-box:8000/v1/",
        });

        var client = (FakeChatClient)app.GetRequiredService<IChatClient>();

        Assert.Equal("https://my-gpu-box:8000/v1/", client.Endpoint);
    }
}
