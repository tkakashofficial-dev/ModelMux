using Microsoft.Extensions.Options;
using ModelMux.Providers;

namespace ModelMux.Core.Tests;

public class CapabilityTests
{
    private static ModelMuxRouter Router(ModelProfile profile, string name = "fast")
    {
        var options = new ModelMuxOptions();
        options.Profiles[name] = profile;

        return new ModelMuxRouter(Options.Create(options), [new FakeProvider(profile.Provider)]);
    }

    [Fact]
    public void Defaults_are_the_openai_protocol_baseline()
    {
        var router = Router(new ModelProfile { Provider = "Gemini", Model = "gemini-2.5-flash" });

        var caps = router.GetCapabilities();

        Assert.True(caps.Streaming);
        Assert.True(caps.ToolCalling);
        Assert.True(caps.StructuredOutput);

        // Vision is far from universal, so it is opt-in rather than assumed.
        Assert.False(caps.Vision);
    }

    [Fact]
    public void A_profile_can_override_the_defaults()
    {
        var router = Router(new ModelProfile
        {
            Provider = "Ollama",
            Model = "tinyllama",
            Capabilities = new ModelCapabilities
            {
                Streaming = true,
                ToolCalling = false,
                StructuredOutput = false,
                ContextWindow = 2048,
            },
        });

        var caps = router.GetCapabilities();

        Assert.False(caps.ToolCalling);
        Assert.False(caps.StructuredOutput);
        Assert.Equal(2048, caps.ContextWindow);
    }

    [Fact]
    public void RequireCapability_passes_when_supported()
    {
        var router = Router(new ModelProfile { Provider = "Gemini", Model = "gemini-2.5-flash" });

        // Should not throw.
        router.RequireCapability(nameof(ModelCapabilities.Streaming));
    }

    [Fact]
    public void RequireCapability_throws_with_the_profile_the_model_and_the_fix()
    {
        var router = Router(new ModelProfile
        {
            Provider = "Ollama",
            Model = "tinyllama",
            Capabilities = new ModelCapabilities { ToolCalling = false },
        });

        var ex = Assert.Throws<UnsupportedCapabilityException>(
            () => router.RequireCapability(nameof(ModelCapabilities.ToolCalling)));

        Assert.Equal("fast", ex.ProfileName);
        Assert.Equal("tinyllama", ex.Model);
        Assert.Contains("ToolCalling", ex.Message, StringComparison.Ordinal);
        Assert.Contains("ModelMux:Profiles:fast:Capabilities", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Vision_must_be_opted_into_per_profile()
    {
        var visionCapable = Router(new ModelProfile
        {
            Provider = "OpenAI",
            Model = "gpt-5",
            Capabilities = new ModelCapabilities { Vision = true },
        });

        Assert.True(visionCapable.GetCapabilities().Vision);
        visionCapable.RequireCapability(nameof(ModelCapabilities.Vision));
    }

    [Theory]
    [InlineData("streaming", true)]
    [InlineData("STREAMING", true)]
    [InlineData("Vision", false)]
    [InlineData("nonsense", false)]
    public void Supports_matches_case_insensitively_and_rejects_unknown_names(
        string capability,
        bool expected)
    {
        Assert.Equal(expected, new ModelCapabilities().Supports(capability));
    }

    [Fact]
    public void Grok_is_registered_out_of_the_box()
    {
        var names = KnownProviders.CreateDefaults().Select(p => p.Name).ToList();

        Assert.Contains(KnownProviders.Grok, names);
        Assert.Equal("https://api.x.ai/v1", KnownProviders.GrokEndpoint.ToString());
    }
}
