# How ModelMux works

Internals, for anyone using ModelMux beyond the README or thinking about contributing to it.

If you only want to *use* the library, the [README](../README.md) is enough. This document
explains the machinery underneath and the reasoning behind it — useful when you're debugging
something, writing a custom provider, or deciding whether the design fits your application.

For the decisions themselves — what was built, what wasn't, and why — see
[architecture-decisions.md](architecture-decisions.md).

---

## Contents

1. [The layer ModelMux occupies](#1-the-layer-modelmux-occupies)
2. [Request lifecycle](#2-request-lifecycle)
3. [Profiles and resolution](#3-profiles-and-resolution)
4. [Providers](#4-providers)
5. [The decorator pipeline](#5-the-decorator-pipeline)
6. [Client caching](#6-client-caching)
7. [Error classification](#7-error-classification)
8. [Capabilities](#8-capabilities)
9. [Cost accounting](#9-cost-accounting)
10. [Attribution](#10-attribution)
11. [Streaming](#11-streaming)
12. [Extending ModelMux](#12-extending-modelmux)
13. [Testing approach](#13-testing-approach)
14. [The reporting sample's safety model](#14-the-reporting-samples-safety-model)

---

## 1. The layer ModelMux occupies

`Microsoft.Extensions.AI` defines `IChatClient`, the abstraction every .NET AI provider
implements. ModelMux does not replace it, wrap it in a new interface, or hide it. Application
code receives an ordinary `IChatClient`.

What ModelMux adds is the layer above: deciding *which* implementation that is, based on
configuration rather than code.

```
Application            depends on IChatClient
     │
ModelMux router        profile name → provider + model
     │
Decorators             error mapping, cost tracking
     │
Microsoft.Extensions.AI
     │
Provider               OpenAI · Gemini · Grok · Ollama · any OpenAI-compatible endpoint
```

Nothing below the application layer is visible to business code. That is the design constraint
everything else follows from.

---

## 2. Request lifecycle

What happens on the first call through a profile:

1. Application resolves `IChatClient` (or calls `IModelMux.GetClient(name)`).
2. Router resolves the profile name, falling back to the default profile.
3. Router reads the `ModelProfile` — provider, model, endpoint, credential source.
4. Router looks up the registered `IChatProvider` matching `Provider`.
5. Provider constructs a real client for that endpoint and model.
6. Registered decorators wrap it, innermost first.
7. The finished client is cached against the profile name.
8. The call executes.

Steps 2–7 happen once per profile. Subsequent calls resolve from cache.

---

## 3. Profiles and resolution

A profile is a named entry in configuration:

```jsonc
"ModelMux": {
  "DefaultProfile": "summarisation",
  "Profiles": {
    "summarisation": {
      "Provider": "Gemini",
      "Model": "gemini-2.5-flash",
      "ApiKeyEnvironmentVariable": "GEMINI_API_KEY",
      "Description": "Bulk summarisation. Quality is sufficient and volume is high."
    }
  }
}
```

### Profile names are yours

ModelMux never inspects a profile name or infers meaning from it. `summarisation`,
`fast`, `tier-2`, and `x` behave identically — they are dictionary keys, matched
case-insensitively. This is verified by
[`ProfileNamingTests`](../tests/ModelMux.Core.Tests/ProfileNamingTests.cs), which includes a
profile literally named `banana` and one where `"fast"` deliberately points at a slow model.

Names that describe the *job* tend to age better than names that describe the vendor, because
`"summarisation"` still reads correctly after you change providers while `"gemini-client"`
becomes a lie. That is a convention, not a requirement.

Because names are strings, declaring constants avoids typos and gives you IntelliSense — the
same approach commonly used with `IHttpClientFactory`:

```csharp
public static class AiProfiles
{
    /// <summary>Bulk summarisation. High volume, quality is sufficient.</summary>
    public const string Summarisation = "summarisation";

    /// <summary>Extraction where accuracy matters more than cost.</summary>
    public const string Extraction = "extraction";
}

var client = mux.GetClient(AiProfiles.Summarisation);
```

### Default profile

`DefaultProfile` names the profile used when a caller doesn't specify one. It may be omitted
when exactly one profile exists — that profile becomes the default. With two or more profiles
and no default, construction fails at startup rather than picking arbitrarily.

### Credentials

`ApiKeyEnvironmentVariable` names an environment variable holding the key. A literal `ApiKey`
field exists for local development but lands in configuration files; the environment variable
wins when both are set. See [SECURITY.md](../SECURITY.md).

---

## 4. Providers

An `IChatProvider` turns a profile into an `IChatClient`:

```csharp
public interface IChatProvider
{
    string Name { get; }
    IChatClient CreateClient(string profileName, ModelProfile profile);
}
```

### One implementation covers four vendors

OpenAI, Google Gemini, xAI Grok, and Ollama all expose the OpenAI chat-completions protocol,
so all four are served by a single `OpenAICompatibleProvider` differing only in endpoint:

| Provider | Endpoint | Credential |
|---|---|---|
| `OpenAI` | SDK default | required |
| `Gemini` | `generativelanguage.googleapis.com/v1beta/openai/` | required |
| `Grok` | `api.x.ai/v1` | required |
| `Ollama` | `localhost:11434/v1/` | not required |

Endpoints were taken from each vendor's published documentation; sources are in
[`KnownProviders`](../src/ModelMux.Core/Providers/KnownProviders.cs).

The consequence worth knowing: **anything else speaking that protocol works without new code.**
vLLM, LM Studio, LocalAI, and self-hosted deployments are reached by setting `Endpoint`:

```jsonc
"on-premise": {
  "Provider": "OpenAI",
  "Model": "llama-3-70b",
  "Endpoint": "https://inference.internal:8000/v1/"
}
```

Providers with a genuinely different wire format — Anthropic's native API, AWS Bedrock —
require their own `IChatProvider`. See [§12](#12-extending-modelmux).

---

## 5. The decorator pipeline

Cross-cutting behaviour is added through `IChatClientDecorator`:

```csharp
public interface IChatClientDecorator
{
    IChatClient Decorate(string profileName, ModelProfile profile, IChatClient client);
}
```

Each decorator wraps the previous one, so **the last registered is outermost**:

```
caller → cost tracking → error mapping → provider client
         (registered     (registered
          last)           first)
```

Ordering is deliberate. Error mapping sits innermost so it observes raw provider exceptions
before anything else can wrap them. Cost tracking sits outermost so its recorded duration
covers everything inside it.

Decorators are the extension point that fallback and caching will use, rather than each
provider growing awareness of concerns that aren't its job.

---

## 6. Client caching

Clients are created once per profile and reused for the router's lifetime:

```csharp
var lazy = _clients.GetOrAdd(
    name,
    key => new Lazy<IChatClient>(() => CreateClient(key), LazyThreadSafetyMode.ExecutionAndPublication));
```

Two details matter here.

**Why cache at all.** Provider clients own HTTP connection pools. Constructing one per request
exhausts sockets under load — a failure that appears only in production.

**Why `Lazy` inside the dictionary.** `ConcurrentDictionary.GetOrAdd` does not guarantee its
value factory runs only once; under concurrent first access, two threads can both build a
client. One is discarded, but it has already opened connections. `Lazy` with
`ExecutionAndPublication` guarantees construction happens exactly once.

Disposing the router disposes every client it created, skipping any `Lazy` whose value was
never realised.

---

## 7. Error classification

Provider exceptions are translated into `ModelMuxProviderException`, carrying a vendor-neutral
category and a retryability flag:

```csharp
catch (ModelMuxProviderException ex) when (ex.IsRetryable)
{
    // RateLimit, Timeout, ProviderUnavailable
}
catch (ModelMuxProviderException ex)
{
    logger.LogError(ex, "{Provider} failed: {Category}", ex.Provider, ex.Category);
}
```

| HTTP | Category | Retryable |
|---|---|---|
| 401, 403 | `AuthenticationFailure` | no |
| 400, 404 | `InvalidRequest` | no |
| 408 | `Timeout` | yes |
| 422 | `ContentFiltered` | no |
| 429 | `RateLimit` | yes |
| 5xx | `ProviderUnavailable` | yes |
| — | `Unknown` | no |

Three properties of this design are load-bearing:

- **The original exception is always the inner exception.** Classification adds information; it
  never discards the provider's own detail.
- **`Unknown` is never retryable.** Guessing retryability on an unclassified failure invites
  retry loops against something permanently broken.
- **Caller cancellation is not translated.** An `OperationCanceledException` raised because the
  caller's token fired remains exactly that, so cancellation-aware code keeps working.

Note that classification does not *act*. Nothing retries. That is a v0.2 concern.

---

## 8. Capabilities

Models differ in what they accept. `ModelCapabilities` records this per profile, defaulting to
the OpenAI-protocol baseline and overridable in configuration:

```jsonc
"on-premise-small": {
  "Provider": "Ollama",
  "Model": "tinyllama",
  "Capabilities": { "ToolCalling": false, "StructuredOutput": false, "ContextWindow": 2048 }
}
```

```csharp
mux.RequireCapability("StructuredOutput", "on-premise-small");
// throws UnsupportedCapabilityException before any network call
```

Capabilities are **declared, not detected**. A hardcoded model→capability table would be stale
within weeks of the next model release, and runtime probing costs a request while still being
incomplete. Declaring them in configuration puts the knowledge where it can be corrected
without waiting for a package update — at the cost that a configuration that lies will be
believed, and the provider will reject the request.

`Vision` defaults to `false` because it is not universal; the rest default to `true`.

---

## 9. Cost accounting

`ModelMux.Cost` computes cost from reported token counts and a price table:

```csharp
cost = (uncachedInput  * price.InputPerMillion
      + cachedInput    * price.CachedInputPerMillion
      + cacheWrite     * price.CacheWritePerMillion
      + outputTokens   * price.OutputPerMillion) / 1_000_000m;
```

`decimal` throughout — binary floating point accumulates rounding error across many small
amounts.

Prices ship for 57 models across Anthropic, OpenAI and Google, each carrying `LastVerified` and
a `Source` URL. Matching is exact first, then longest-prefix, so `claude-opus-5-20260101`
resolves against a `claude-opus-5` entry. Prefix collisions are covered by
[`BuiltInPricingTests`](../tests/ModelMux.Cost.Tests/BuiltInPricingTests.cs) — `gpt-5-mini`
must not resolve to `gpt-5`'s price.

Two rules govern how cost is reported:

**Cost is stored, not recomputed.** Prices change. A record recomputed at read time would
silently restate history whenever a vendor adjusted pricing.

**An unpriced model records `null`, never `0`.** Zero means free; null means unknown.
Conflating them makes a total quietly understate real spend. Unpriced calls surface as
`UsageSummary.UnpricedCount` so a total can be labelled a lower bound.

Estimated token counts — used when a provider reports none, common with local models — are
flagged via `IsEstimated` and counted separately, so estimates are never silently mixed into a
figure presented as measured.

Failures in the usage store are logged and swallowed. Telemetry must not be able to break the
call it measures.

### Overriding prices

Provider prices change faster than package releases. Override without waiting:

```jsonc
"ModelMux": {
  "Cost": {
    "Pricing": [
      { "Model": "gpt-4o-mini", "InputPerMillion": 0.15, "OutputPerMillion": 0.60,
        "Currency": "USD", "LastVerified": "2026-08-16", "Source": "https://openai.com/api/pricing/" }
    ]
  }
}
```

---

## 10. Attribution

Usage is attributed through an ambient scope that flows across `await` boundaries:

```csharp
using (UsageScope.Begin(tenantId: "acme-corp", feature: "invoice-extraction"))
{
    await ai.GetResponseAsync(messages);      // attributed, however deep the call stack
}
```

This uses `AsyncLocal<T>`, so concurrent requests each see their own value — a `static` field
would leak one tenant's context into another's request. Nested scopes inherit values they
don't override, and disposal restores the previous scope.

When no scope is open, usage is attributed to the **profile name**, so per-profile cost is
available with no caller effort.

If your application already resolves the current tenant, implement
`IUsageAttributionAccessor` and register it. ModelMux will use yours, and callers won't need to
open scopes at all:

```csharp
services.AddSingleton<IUsageAttributionAccessor, MyTenantContextAdapter>();
```

---

## 11. Streaming

Streaming flows through unchanged; both decorators handle it.

The detail worth knowing: **token usage arrives in a trailing update**, not the first one.
Cost tracking accumulates `UsageContent` across the whole stream rather than reading the head:

```csharp
foreach (var content in update.Contents)
    if (content is UsageContent usage)
        merged = Merge(merged, usage.Details);
```

Usage is also recorded when a consumer abandons a stream early — those tokens were generated
and billed regardless of whether anyone read them.

---

## 12. Extending ModelMux

### A provider with a different wire format

```csharp
public sealed class AnthropicNativeProvider : IChatProvider
{
    public string Name => "Anthropic";

    public IChatClient CreateClient(string profileName, ModelProfile profile)
    {
        var key = profile.ResolveApiKey()
            ?? throw new ModelMuxConfigurationException(
                $"Profile '{profileName}' needs an API key. Set "
                + $"ModelMux:Profiles:{profileName}:ApiKeyEnvironmentVariable.");

        return new MyAnthropicChatClient(key, profile.Model);
    }
}
```

```csharp
builder.Services.AddModelMux(builder.Configuration)
    .AddProvider(new AnthropicNativeProvider());
```

Registering a provider whose `Name` matches a built-in one replaces it — useful for swapping an
implementation without forking.

### An OpenAI-compatible endpoint under its own name

```csharp
builder.Services.AddModelMux(builder.Configuration)
    .AddOpenAICompatibleProvider("Groq", new Uri("https://api.groq.com/openai/v1"));
```

### A custom decorator

```csharp
internal sealed class LoggingDecorator(ILoggerFactory factory) : IChatClientDecorator
{
    public IChatClient Decorate(string profileName, ModelProfile profile, IChatClient client) =>
        new MyLoggingChatClient(client, factory.CreateLogger(profileName));
}

services.AddSingleton<IChatClientDecorator, LoggingDecorator>();
```

Return the client unchanged to opt out for a given profile. Returning `null` is an error.

### A different usage store

Implement `IUsageStore` to send usage to a warehouse, queue, or existing metrics pipeline.
Implement `IUsageQuery` as well if you want to read it back. Implementations must not throw.

---

## 13. Testing approach

The suite is deliberately split.

**Unit tests (159)** use a fake `IChatClient` that reports which provider produced it, so
routing can be asserted without a network call. They run in about two seconds, cost nothing,
and cannot fail because a vendor had an outage.

**Live provider tests (5)** make real HTTP calls and skip themselves unless the matching
credential is present:

```csharp
[SkippableFact]
public async Task Gemini_answers_over_its_openai_compatible_endpoint()
{
    Skip.If(Key("GEMINI_API_KEY") is null, "GEMINI_API_KEY is not set.");
    ...
}
```

```bash
GEMINI_API_KEY=... dotnet test      # runs them
dotnet test                         # skips them
```

CI holds no credentials, so it runs the fast suite only. Fakes verify the wiring; live tests
verify the wire format. Both are needed, and only one belongs in a pipeline.

Registration faults — ambiguous constructors, missing services — cannot be caught by tests that
construct objects by hand, so
[`DependencyInjectionTests`](../tests/ModelMux.Cost.Tests/DependencyInjectionTests.cs) builds a
real `ServiceProvider`.

---

## 14. The reporting sample's safety model

`samples/ModelMux.WebDemo` includes a natural-language reporting endpoint. Its design is worth
reading before building anything similar, because the naive version is dangerous:

```csharp
// Do not do this.
var sql = await ai.GetResponseAsync($"Write SQL for: {userQuestion}");
await db.ExecuteAsync(sql);
```

That is SQL injection with a natural-language front end.

The sample instead constrains the model to emit a **structured intent** restricted to an
allowlist of reports, fields, and operators:

```
question → model proposes a ReportIntent
         → application validates every field, operator and value type
         → application executes with predicates selected by switch
         → intent returned to the caller, so the decision is inspectable
```

Model output is data, never code. A hallucinated field or unknown operator produces a
validation failure, not an incident. `EmployeeRepository.Execute` re-validates and throws
rather than trusting its caller, so forgetting to validate fails loudly.

[19 tests](../tests/ModelMux.WebDemo.Tests/IntentValidatorTests.cs) cover the adversarial cases:
invented fields, injected operators, unparseable values, and filter floods.

---

## Reference

| Concern | Source |
|---|---|
| Profile resolution and caching | [`ModelMuxRouter.cs`](../src/ModelMux.Core/ModelMuxRouter.cs) |
| Provider contract | [`IChatProvider.cs`](../src/ModelMux.Core/IChatProvider.cs) |
| OpenAI-protocol provider | [`OpenAICompatibleProvider.cs`](../src/ModelMux.Core/Providers/OpenAICompatibleProvider.cs) |
| Built-in endpoints | [`KnownProviders.cs`](../src/ModelMux.Core/Providers/KnownProviders.cs) |
| Decorator contract | [`IChatClientDecorator.cs`](../src/ModelMux.Core/IChatClientDecorator.cs) |
| Error classification | [`ErrorMappingChatClient.cs`](../src/ModelMux.Core/Errors/ErrorMappingChatClient.cs) |
| Capabilities | [`ModelCapabilities.cs`](../src/ModelMux.Core/ModelCapabilities.cs) |
| Cost middleware | [`CostTrackingChatClient.cs`](../src/ModelMux.Cost/CostTrackingChatClient.cs) |
| Price table | [`BuiltInPricing.cs`](../src/ModelMux.Cost/Pricing/BuiltInPricing.cs) |
| Attribution scope | [`UsageScope.cs`](../src/ModelMux.Cost/Attribution/UsageScope.cs) |
