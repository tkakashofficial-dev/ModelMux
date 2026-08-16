using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelMux;

// ---------------------------------------------------------------------------
// Setup. Everything about which vendor serves which profile lives in
// appsettings.json — nothing below this block mentions a provider.
// ---------------------------------------------------------------------------
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddModelMux(configuration);
services.AddSingleton<ReportService>();

using var provider = services.BuildServiceProvider();

var mux = provider.GetRequiredService<IModelMux>();
var reports = provider.GetRequiredService<ReportService>();

// ---------------------------------------------------------------------------
// Show how each logical profile is currently wired.
// ---------------------------------------------------------------------------
Console.WriteLine("ModelMux sample");
Console.WriteLine(new string('-', 78));
Console.WriteLine($"  {"PROFILE",-12}{"PROVIDER",-12}{"MODEL",-26}{"ENDPOINT",-28}");
Console.WriteLine(new string('-', 78));

foreach (var name in mux.ProfileNames)
{
    var profile = mux.GetProfile(name);
    var isDefault = string.Equals(name, mux.DefaultProfileName, StringComparison.OrdinalIgnoreCase);
    var endpoint = profile.Endpoint ?? "(provider default)";

    Console.WriteLine(
        $"  {name + (isDefault ? " *" : ""),-12}{profile.Provider,-12}{profile.Model,-26}{endpoint,-28}");
}

Console.WriteLine(new string('-', 78));
Console.WriteLine("  * = default profile, used when application code doesn't name one");
Console.WriteLine();

// ---------------------------------------------------------------------------
// The point of the demo: ReportService never names a provider. Re-point a
// profile in appsettings.json, run again, and this code is untouched.
// ---------------------------------------------------------------------------
Console.WriteLine("Calling ReportService (which only knows about IChatClient):");
Console.WriteLine();

foreach (var profileName in mux.ProfileNames)
{
    Console.Write($"  via '{profileName}' … ");

    try
    {
        var summary = await reports.SummariseAsync(
            "Q3 revenue rose 12% while support tickets fell 4%.",
            profileName);

        Console.WriteLine(Truncate(summary, 60));
    }
    catch (ModelMuxConfigurationException ex)
    {
        Console.WriteLine($"not configured — {ex.Message.Split('.')[0]}.");
    }
    catch (Exception ex)
    {
        // A live call needs a key and network; the routing above still demonstrates the point.
        Console.WriteLine($"call failed ({ex.GetType().Name}) — set an API key to run for real.");
    }
}

Console.WriteLine();
Console.WriteLine("Change a Provider in appsettings.json and run again.");
Console.WriteLine("ReportService.cs does not change.");

static string Truncate(string text, int max) =>
    text.Length <= max ? text : text[..max] + "…";

/// <summary>
/// Stand-in for real application code. It depends on <see cref="IChatClient"/> and on
/// <see cref="IModelMux"/> only to pick a profile by name — never on a vendor SDK.
/// </summary>
internal sealed class ReportService(IModelMux mux)
{
    public async Task<string> SummariseAsync(string data, string? profile = null)
    {
        var ai = mux.GetClient(profile);

        var response = await ai.GetResponseAsync(
            [new ChatMessage(ChatRole.User, $"Summarise in one sentence: {data}")]);

        return response.Text;
    }
}
