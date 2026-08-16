using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ModelMux.Core.Tests;

/// <summary>
/// Real calls against real providers. These are the only tests that touch the network, and
/// each one <b>skips itself</b> unless the matching API key is present in the environment.
/// </summary>
/// <remarks>
/// <para>
/// CI runs with no keys, so these skip there and the pipeline stays fast, free, and immune to
/// a provider outage. Run them locally when you want to confirm the wire format is still
/// right:
/// </para>
/// <code>
/// $env:GEMINI_API_KEY = "..."      # then: dotnet test
/// </code>
/// <para>
/// They exist because every other test in this repository uses a fake provider. Fakes prove
/// the wiring; only these prove ModelMux can actually talk to a vendor.
/// </para>
/// </remarks>
public class LiveProviderTests
{
    private static string? Key(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } key ? key : null;

    private static IModelMux BuildMux(string provider, string model, string keyVariable, string? endpoint = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ModelMux:Profiles:live:Provider"] = provider,
            ["ModelMux:Profiles:live:Model"] = model,
            ["ModelMux:Profiles:live:ApiKeyEnvironmentVariable"] = keyVariable,
        };

        if (endpoint is not null)
        {
            settings["ModelMux:Profiles:live:Endpoint"] = endpoint;
        }

        var services = new ServiceCollection();
        services.AddModelMux(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        return services.BuildServiceProvider().GetRequiredService<IModelMux>();
    }

    private static async Task AssertAnswersAsync(IModelMux mux)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var response = await mux.GetClient().GetResponseAsync(
            [new ChatMessage(ChatRole.User, "Reply with exactly the word: pong")],
            cancellationToken: cts.Token);

        Assert.False(string.IsNullOrWhiteSpace(response.Text));
        Assert.Contains("pong", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task Gemini_answers_over_its_openai_compatible_endpoint()
    {
        Skip.If(Key("GEMINI_API_KEY") is null, "GEMINI_API_KEY is not set.");

        await AssertAnswersAsync(BuildMux("Gemini", "gemini-2.5-flash", "GEMINI_API_KEY"));
    }

    [SkippableFact]
    public async Task OpenAI_answers()
    {
        Skip.If(Key("OPENAI_API_KEY") is null, "OPENAI_API_KEY is not set.");

        await AssertAnswersAsync(BuildMux("OpenAI", "gpt-5-mini", "OPENAI_API_KEY"));
    }

    [SkippableFact]
    public async Task Grok_answers()
    {
        Skip.If(Key("XAI_API_KEY") is null, "XAI_API_KEY is not set.");

        await AssertAnswersAsync(BuildMux("Grok", "grok-4.6", "XAI_API_KEY"));
    }

    [SkippableFact]
    public async Task Ollama_answers_from_a_local_server()
    {
        // Opt in explicitly: a local Ollama needs no key, so there is no credential to
        // detect. Set MODELMUX_TEST_OLLAMA=1 once the server is running.
        Skip.If(Key("MODELMUX_TEST_OLLAMA") is null, "MODELMUX_TEST_OLLAMA is not set.");

        var model = Environment.GetEnvironmentVariable("MODELMUX_TEST_OLLAMA_MODEL") ?? "llama3";

        await AssertAnswersAsync(BuildMux("Ollama", model, "UNUSED"));
    }

    [SkippableFact]
    public async Task A_bad_key_surfaces_as_an_AuthenticationFailure_not_a_raw_provider_error()
    {
        // Proves the error mapping works against a real provider response, not just a
        // synthetic exception in a unit test.
        Skip.If(Key("GEMINI_API_KEY") is null, "GEMINI_API_KEY is not set.");

        const string variable = "MODELMUX_DELIBERATELY_BAD_KEY";
        Environment.SetEnvironmentVariable(variable, "definitely-not-a-valid-key");

        try
        {
            var mux = BuildMux("Gemini", "gemini-2.5-flash", variable);

            var ex = await Assert.ThrowsAsync<ModelMuxProviderException>(
                () => mux.GetClient().GetResponseAsync([new ChatMessage(ChatRole.User, "hi")]));

            Assert.Equal(AiErrorCategory.AuthenticationFailure, ex.Category);
            Assert.False(ex.IsRetryable);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}
