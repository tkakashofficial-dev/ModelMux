using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ModelMux.Core.Tests;

/// <summary>
/// Profile names are arbitrary strings chosen by the application, not keywords ModelMux
/// understands.
/// </summary>
/// <remarks>
/// The names in the README — <c>fast</c>, <c>smart</c>, <c>private</c> — are a documentation
/// convention, nothing more. ModelMux never inspects a profile name to infer behaviour, and
/// these tests exist so nobody has to read the source to be sure of that.
/// </remarks>
public class ProfileNamingTests
{
    private static IModelMux Build(string profileName, string provider = "Gemini")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = profileName,
            [$"ModelMux:Profiles:{profileName}:Provider"] = provider,
            [$"ModelMux:Profiles:{profileName}:Model"] = "some-model",
        }).Build();

        var services = new ServiceCollection();
        services.AddModelMux(config).AddProvider(new FakeProvider(provider));

        return services.BuildServiceProvider().GetRequiredService<IModelMux>();
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("smart")]
    [InlineData("private")]
    [InlineData("reasoning")]
    // Nonsense names must behave identically. If any of these failed, it would mean the
    // library was secretly treating certain names as special.
    [InlineData("banana")]
    [InlineData("customer-facing-summariser")]
    [InlineData("profile-1")]
    [InlineData("ProdEuropeWest")]
    [InlineData("x")]
    [InlineData("the_one_finance_approved")]
    public void Any_name_works_and_none_is_treated_specially(string profileName)
    {
        var mux = Build(profileName);

        Assert.Equal(profileName, mux.DefaultProfileName);
        Assert.NotNull(mux.GetClient(profileName));
        Assert.Equal("Gemini", mux.GetClient(profileName).AsFake().ProviderName);
    }

    [Fact]
    public void A_name_carries_no_meaning_beyond_being_a_key()
    {
        // "fast" pointed at a slow model, and "slow" pointed at a fast one, work fine.
        // ModelMux does not read the name and does not care that it is misleading.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ModelMux:DefaultProfile"] = "fast",
            ["ModelMux:Profiles:fast:Provider"] = "Gemini",
            ["ModelMux:Profiles:fast:Model"] = "an-extremely-slow-model",
            ["ModelMux:Profiles:slow:Provider"] = "Gemini",
            ["ModelMux:Profiles:slow:Model"] = "the-fastest-model",
        }).Build();

        var services = new ServiceCollection();
        services.AddModelMux(config).AddProvider(new FakeProvider("Gemini"));
        var mux = services.BuildServiceProvider().GetRequiredService<IModelMux>();

        Assert.Equal("an-extremely-slow-model", mux.GetProfile("fast").Model);
        Assert.Equal("the-fastest-model", mux.GetProfile("slow").Model);
    }

    [Fact]
    public void A_single_profile_needs_no_name_convention_at_all()
    {
        // The simplest possible setup: one profile, any name, no DefaultProfile needed.
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ModelMux:Profiles:whatever:Provider"] = "Gemini",
            ["ModelMux:Profiles:whatever:Model"] = "gemini-2.5-flash",
        }).Build();

        var services = new ServiceCollection();
        services.AddModelMux(config).AddProvider(new FakeProvider("Gemini"));
        var mux = services.BuildServiceProvider().GetRequiredService<IModelMux>();

        Assert.Equal("whatever", mux.DefaultProfileName);
    }
}
