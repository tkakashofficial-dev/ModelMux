# How ModelMux works — a guide to what you built

This is a learning document, not marketing. It walks through every idea in this repository,
in the order you'd need to understand them, with real code from the project.

If you can explain the sections in **Part 5** out loud, you can defend this project in any
interview.

---

## Contents

1. [The problem, in one page](#part-1--the-problem-in-one-page)
2. [The .NET patterns you used](#part-2--the-net-patterns-you-used)
3. [The AI concepts you used](#part-3--the-ai-concepts-you-used)
4. [Walking the code](#part-4--walking-the-code)
5. [Explaining this in an interview](#part-5--explaining-this-in-an-interview)
6. [What to learn next](#part-6--what-to-learn-next)

---

## Part 1 — The problem, in one page

### The situation

You add AI to a .NET app. You pick Gemini. Six months later OpenAI is better, or cheaper, or
your company decides data can't leave the building and you need a local model.

### The naive version

```csharp
public class ReportService
{
    private readonly GeminiClient _gemini = new("api-key");   // ← locked in

    public async Task<string> SummariseAsync(string data)
    {
        var response = await _gemini.GenerateAsync(data);
        return response.Text;
    }
}
```

Switching providers means editing this class. And every other class like it. In a large app
that's hundreds of files.

### What Microsoft already fixed

`Microsoft.Extensions.AI` gives .NET one interface — `IChatClient` — that every provider
implements:

```csharp
public class ReportService(IChatClient ai)          // ← depends on the interface
{
    public async Task<string> SummariseAsync(string data)
    {
        var response = await ai.GetResponseAsync([new ChatMessage(ChatRole.User, data)]);
        return response.Text;
    }
}
```

Now `ReportService` doesn't know or care which provider is behind it. **This is the single
most important thing to understand about .NET AI.** It's the same idea as `ILogger` — your code
logs, and something else decides whether that goes to a file, a console, or Seq.

### What was still missing

Somebody still has to *create* the right `IChatClient`. Out of the box you hand-write this in
every project:

```csharp
builder.Services.AddChatClient(new OpenAIClient(key).GetChatClient("gpt-5").AsIChatClient());
//                                  ↑ still hardcoded, still needs a redeploy to change
```

**ModelMux is that factory, written once, driven by configuration.** That's the whole product
in one sentence.

---

## Part 2 — The .NET patterns you used

These are general .NET skills. They'll come up in interviews about *any* codebase, not just
this one.

### 2.1 Dependency Injection — the shape of the API

DI is a container that builds your objects for you. You say "when someone asks for
`IChatClient`, give them this", and the container wires everything up.

Look at what `AddModelMux` actually registers ([`ModelMuxServiceCollectionExtensions.cs`](../src/ModelMux.Core/DependencyInjection/ModelMuxServiceCollectionExtensions.cs)):

```csharp
services.AddSingleton<IChatClientDecorator, ErrorMappingDecorator>();
services.TryAddSingleton<ModelMuxRouter>();
services.TryAddSingleton<IModelMux>(sp => sp.GetRequiredService<ModelMuxRouter>());
services.TryAddSingleton<IChatClient>(sp => sp.GetRequiredService<IModelMux>().GetClient());
```

Three things worth knowing here:

**`AddSingleton` vs `TryAddSingleton`.** `Add` always registers. `TryAdd` registers *only if
nobody already did*. We use `TryAdd` so an application that registered its own store or
estimator before calling `AddModelMux` keeps theirs. That's a small courtesy that makes a
library feel well-built.

**Lifetimes.** `Singleton` = one instance for the whole app. `Scoped` = one per HTTP request.
`Transient` = a new one every time. ModelMux uses singletons because provider clients hold HTTP
connection pools — creating one per request would exhaust your sockets. **This is a classic
interview question.**

**Factory registration.** That last line registers `IChatClient` as "whatever the router's
default profile returns". This is why existing code that injects `IChatClient` keeps working
with zero changes.

> **A bug this caused.** `PricingResolver` has two public constructors. The container couldn't
> pick between them and threw at startup. My unit tests built it by hand so they never saw it —
> only running the sample app caught it. The fix was an explicit factory. **Lesson: unit tests
> that bypass the container can't catch container problems.** That's why
> [`DependencyInjectionTests`](../tests/ModelMux.Cost.Tests/DependencyInjectionTests.cs) builds
> a real `ServiceProvider`.

### 2.2 The Options pattern — configuration into objects

.NET binds JSON config onto C# classes:

```jsonc
{ "ModelMux": { "DefaultProfile": "fast", "Profiles": { "fast": { "Provider": "Gemini" } } } }
```

```csharp
services.AddOptions<ModelMuxOptions>().Bind(configuration.GetSection("ModelMux"));
```

The property names must match the JSON keys. Then you inject `IOptions<ModelMuxOptions>` and
read `.Value`.

```csharp
public ModelMuxRouter(IOptions<ModelMuxOptions> options, ...)
{
    _options = options.Value;
}
```

**Why this matters for your product:** because config is bound at startup rather than compiled
in, changing `"Provider": "Gemini"` to `"Provider": "OpenAI"` needs a restart, not a rebuild.
That *is* the feature.

### 2.3 The Decorator pattern — the heart of the library

A decorator wraps an object in another object with the *same interface*, adding behaviour.

```
Your code  →  CostTracking  →  ErrorMapping  →  real Gemini client
              (records $)      (classifies)     (does the work)
```

Every layer is an `IChatClient`. Your code can't tell how many layers there are.

`Microsoft.Extensions.AI` ships a base class for this — `DelegatingChatClient`:

```csharp
public sealed class CostTrackingChatClient : DelegatingChatClient
{
    public override async Task<ChatResponse> GetResponseAsync(...)
    {
        var start = _timeProvider.GetTimestamp();

        var response = await base.GetResponseAsync(...);   // ← call the inner client

        await RecordAsync(...);                            // ← then do our extra work
        return response;
    }
}
```

`base.GetResponseAsync` calls whatever is inside. Everything before and after is your addition.

This is the same pattern as ASP.NET Core middleware, and as `DelegatingHandler` in `HttpClient`.
**If you understand one, you understand all three.** Say that in an interview.

We made it an extension point ([`IChatClientDecorator`](../src/ModelMux.Core/IChatClientDecorator.cs)) so
cost tracking, and later caching and fallback, all plug into the same hook.

**Order matters.** Decorators apply in registration order, each wrapping the previous, so the
*last registered is outermost*. Error mapping registers first (innermost — it sees raw provider
exceptions). Cost tracking registers last (outermost — its stopwatch covers everything inside).

### 2.4 `AsyncLocal` — how the tenant follows the request

This one is genuinely subtle and worth understanding properly.

```csharp
using (UsageScope.Begin(tenantId: "acme", feature: "invoice-extraction"))
{
    await SomeMethod();          // ← 3 layers deep, still knows the tenant is "acme"
}
```

How does code five calls away know the tenant, without it being passed as a parameter?

```csharp
private static readonly AsyncLocal<UsageAttribution?> Ambient = new();
```

`AsyncLocal<T>` is like a static variable **that flows down the async call chain** and is
isolated per logical flow. Two HTTP requests handled at the same time each see their own value.
A `static` field would be shared by both — a serious bug in a multi-tenant app.

The `using` block matters. `Begin` returns an `IDisposable` that restores the previous value:

```csharp
public static IDisposable Begin(string? tenantId = null, ...)
{
    var previous = Ambient.Value;
    Ambient.Value = new UsageAttribution { TenantId = tenantId ?? previous?.TenantId, ... };
    return new Restore(previous);          // ← Dispose puts the old value back
}
```

Nested scopes inherit what they don't override. An inner scope can set a feature without
restating the tenant.

> `HttpContextAccessor` in ASP.NET Core works this way too. Same mechanism.

### 2.5 Thread-safe lazy caching

```csharp
private readonly ConcurrentDictionary<string, Lazy<IChatClient>> _clients = new(...);

var lazy = _clients.GetOrAdd(
    name,
    key => new Lazy<IChatClient>(() => CreateClient(key), LazyThreadSafetyMode.ExecutionAndPublication));

return lazy.Value;
```

Why `Lazy<T>` inside a `ConcurrentDictionary` rather than just the client?

Because `GetOrAdd`'s factory **can run more than once** under concurrency. Without `Lazy`, two
threads asking for `"fast"` at the same instant could both build a client — one gets thrown
away, but it already opened connections. Wrapping in `Lazy` with `ExecutionAndPublication`
guarantees the expensive work happens exactly once.

**This is a genuinely good interview answer.** Most people don't know `GetOrAdd` isn't atomic
over its factory.

### 2.6 Async iterators and a C# rule you'll hit

You **cannot** `yield return` inside a `try` block that has a `catch`. The compiler forbids it.

That's a problem when you want to catch errors from a stream. The workaround, from
[`ErrorMappingChatClient.cs`](../src/ModelMux.Core/Errors/ErrorMappingChatClient.cs):

```csharp
var enumerator = base.GetStreamingResponseAsync(...).GetAsyncEnumerator(ct);
try
{
    while (true)
    {
        ChatResponseUpdate update;

        try                                     // ← catch only around MoveNext
        {
            if (!await enumerator.MoveNextAsync()) break;
            update = enumerator.Current;
        }
        catch (Exception ex) { throw Map(ex); }

        yield return update;                    // ← yield is OUTSIDE the catch
    }
}
finally                                          // ← try/finally CAN contain yield
{
    await enumerator.DisposeAsync();
}
```

You drive the enumerator by hand instead of using `await foreach`. `try`/**`finally`** is
allowed with `yield`; `try`/**`catch`** is not.

The `finally` also runs when a consumer **abandons** the stream early (`break` out of the
loop). That's how cost tracking still records usage for a stream nobody finished reading —
those tokens were billed regardless.

### 2.7 Testing without the network

Every unit test here uses a **fake**, not a real provider:

```csharp
internal sealed class FakeChatClient(string providerName, string model, ...) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(...) =>
        Task.FromResult(new ChatResponse(
            new ChatMessage(ChatRole.Assistant, $"served by {ProviderName}/{Model}")));
}
```

Because the fake reports which provider made it, the test can assert *routing* without any API
call. That's why 152 tests run in under two seconds, cost nothing, and can't fail because a
vendor had an outage.

The 5 live tests use `[SkippableFact]` and skip themselves unless a key is present:

```csharp
[SkippableFact]
public async Task Gemini_answers_over_its_openai_compatible_endpoint()
{
    Skip.If(Key("GEMINI_API_KEY") is null, "GEMINI_API_KEY is not set.");
    ...
}
```

**Fakes prove the wiring. Live tests prove the wire format.** You need both, and only one of
them belongs in CI.

---

## Part 3 — The AI concepts you used

### 3.1 What an LLM API call actually is

Strip away the SDK and it's an HTTP POST:

```
POST https://api.openai.com/v1/chat/completions
{ "model": "gpt-5-mini", "messages": [{ "role": "user", "content": "Hello" }] }
```

Response:

```json
{
  "choices": [{ "message": { "role": "assistant", "content": "Hi there!" } }],
  "usage": { "prompt_tokens": 9, "completion_tokens": 4, "total_tokens": 13 }
}
```

That's it. There is no magic. **You are not renting a GPU** — you're calling someone's web API
and paying per token.

### 3.2 Tokens, and why cost is per-token

A token is roughly ¾ of an English word. "Hello world" ≈ 2 tokens.

You pay for **input tokens** (your prompt) and **output tokens** (the reply), at different
rates. Output is always more expensive — usually 4–5×.

Real prices from your catalogue ([`BuiltInPricing.cs`](../src/ModelMux.Cost/Pricing/BuiltInPricing.cs)),
per **million** tokens:

| Model | Input | Output |
|---|---|---|
| `gemini-2.5-flash` | $0.30 | $2.50 |
| `gpt-5-mini` | $0.25 | $2.00 |
| `claude-opus-5` | $5.00 | $25.00 |

The arithmetic in [`CostCalculator.cs`](../src/ModelMux.Cost/Pricing/CostCalculator.cs):

```csharp
cost = (inputTokens * price.InputPerMillion + outputTokens * price.OutputPerMillion) / 1_000_000m;
```

1,000 input + 500 output on `gemini-2.5-flash`:

```
(1000 × 0.30 + 500 × 2.50) / 1,000,000 = (300 + 1250) / 1,000,000 = $0.00155
```

**Note `decimal`, not `double`.** Money is always `decimal` in .NET — `double` has binary
rounding errors that accumulate. Another common interview question.

### 3.3 Prompt caching

Providers cache repeated prompt prefixes and charge ~10% for cache hits. If you send a 50KB
system prompt on every call, caching it saves ~90% of that cost.

Providers report cached tokens separately, which is why `UsageRecord` has `CachedInputTokens`
and the calculator bills three buckets at three rates.

### 3.4 Streaming

Non-streaming: wait 8 seconds, get the whole answer. Streaming: get words as they're generated.

```csharp
await foreach (var update in client.GetStreamingResponseAsync(messages))
{
    Console.Write(update.Text);
}
```

**The gotcha you handled:** token usage arrives in a *trailing* update, not the first one. So
you have to accumulate across the whole stream:

```csharp
foreach (var content in update.Contents)
{
    if (content is UsageContent usageContent)
        usage = Merge(usage, usageContent.Details);      // ← may arrive at the very end
}
```

If you only looked at the first update, every streamed call would record zero cost.

### 3.5 Structured output

Normally a model returns prose. Structured output constrains it to a JSON schema:

```csharp
record Analysis(string Category, double Confidence, string Summary);

var result = await mux.GetStructuredResponseAsync<Analysis>("Analyse this ticket: ...");
```

The library derives a JSON schema from the C# type and tells the provider "your answer must
match this shape". You get a typed object instead of prose you have to parse with regex.

This is what makes the reporting demo possible.

### 3.6 The insight that shaped the whole library

While researching, I checked the docs and found:

| Provider | Endpoint |
|---|---|
| OpenAI | `api.openai.com/v1` |
| **Gemini** | `generativelanguage.googleapis.com/v1beta/openai/` |
| **Grok** | `api.x.ai/v1` |
| **Ollama** | `localhost:11434/v1/` |

**They all speak the OpenAI protocol.** Google, xAI, and Ollama all publish OpenAI-compatible
endpoints because so much tooling already targets that shape.

So instead of four SDK integrations, ModelMux has **one** provider class that differs only in
base URL ([`OpenAICompatibleProvider.cs`](../src/ModelMux.Core/Providers/OpenAICompatibleProvider.cs)).

The payoff: **vLLM, LM Studio, LocalAI, and any rented GPU box also speak it.** Your "move to
our own GPU later" requirement became a config field on day one:

```jsonc
"gpu": { "Provider": "OpenAI", "Model": "llama-3-70b", "Endpoint": "http://gpu-box:8000/v1/" }
```

**Research before coding saved about a week here.** That's the lesson, not the endpoint list.

---

## Part 4 — Walking the code

### The routing path, end to end

```
1. App asks for IChatClient
2. DI calls  IModelMux.GetClient()
3. Router looks up the default profile name
4. Router reads the profile:  { Provider: "Gemini", Model: "gemini-2.5-flash" }
5. Router finds the provider registered under "Gemini"
6. Provider builds an OpenAI client pointed at Gemini's endpoint
7. Decorators wrap it:  error mapping (inner) → cost tracking (outer)
8. Client is cached, so steps 3–7 happen once per profile
9. App calls GetResponseAsync and never knows any of this happened
```

### File by file

| File | What it does |
|---|---|
| [`ModelProfile.cs`](../src/ModelMux.Core/ModelProfile.cs) | One profile: provider, model, endpoint, key, capabilities |
| [`ModelMuxOptions.cs`](../src/ModelMux.Core/ModelMuxOptions.cs) | The config root — all profiles + which is default |
| [`IChatProvider.cs`](../src/ModelMux.Core/IChatProvider.cs) | "Turn a profile into an `IChatClient`" — the extension point for new vendors |
| [`OpenAICompatibleProvider.cs`](../src/ModelMux.Core/Providers/OpenAICompatibleProvider.cs) | The one implementation covering 4 providers |
| [`KnownProviders.cs`](../src/ModelMux.Core/Providers/KnownProviders.cs) | Endpoint constants for OpenAI/Gemini/Grok/Ollama |
| [`ModelMuxRouter.cs`](../src/ModelMux.Core/ModelMuxRouter.cs) | The brain: profile → provider → cached client |
| [`IChatClientDecorator.cs`](../src/ModelMux.Core/IChatClientDecorator.cs) | The hook cost/caching/fallback all plug into |
| [`ErrorMappingChatClient.cs`](../src/ModelMux.Core/Errors/ErrorMappingChatClient.cs) | Vendor exception → `AiErrorCategory` |
| [`ModelCapabilities.cs`](../src/ModelMux.Core/ModelCapabilities.cs) | What a model can do; check before you ask |
| [`CostTrackingChatClient.cs`](../src/ModelMux.Cost/CostTrackingChatClient.cs) | Stopwatch + token read + cost math + save |
| [`UsageScope.cs`](../src/ModelMux.Cost/Attribution/UsageScope.cs) | `AsyncLocal` tenant/feature tagging |

### Two decisions worth understanding deeply

**Cost is stored, not recomputed.**

```csharp
Cost = cost.Cost,          // ← calculated now, saved forever
```

If you recomputed on read, a price change would silently rewrite history. Last month's invoice
would change. Storing it means the record says what the call cost *when it was made*.

**An unpriced model records `null`, never `0`.**

```csharp
public decimal? Cost { get; init; }        // ← nullable on purpose
public long UnpricedCount { get; init; }   // ← surfaced in every summary
```

`0` means "this was free". `null` means "we don't know". Confusing the two makes a cost tool
lie, quietly, in the direction that looks good. That's the worst kind of bug in a finance tool.

### The reporting demo — the security bit

This is the most interesting code in the repo.

**The naive version, which you must never build:**

```csharp
var sql = await ai.GetResponseAsync($"Write SQL for: {userQuestion}");
await db.ExecuteAsync(sql);        // ☠️ SQL injection with a natural-language front end
```

**What you actually built:**

```
user question
   → model proposes a ReportIntent   (constrained to an allowlist)
   → app validates every field/operator/value
   → app executes with LINQ predicates chosen by switch
   → intent returned to caller, so the decision is inspectable
```

The model's output is *data*, never code:

```csharp
public sealed class ReportFilter
{
    public string Field { get; set; }       // must be in the allowlist
    public string Operator { get; set; }    // must be in the allowlist
    public string Value { get; set; }       // must parse as the field's type
}
```

The validator rejects anything else ([`IntentValidator.cs`](../samples/ModelMux.WebDemo/Reporting/IntentValidator.cs)),
and the repository **re-validates and throws** rather than trusting its caller:

```csharp
if (!IntentValidator.Validate(intent).IsValid)
    throw new InvalidOperationException("Refusing to execute an intent that failed validation.");
```

That's defence in depth: even if someone forgets to validate, execution won't proceed.

**The principle to remember: the model proposes, the application disposes.**

---

## Part 5 — Explaining this in an interview

Practise these out loud. If you can't say it, you don't know it yet.

### "What did you build?"

> A .NET library that lets you switch AI providers from configuration instead of code. Your
> business code depends on `IChatClient` and never on a vendor. Changing from Gemini to OpenAI
> to a self-hosted model is an `appsettings.json` edit and a restart.

### "Why not just use Microsoft.Extensions.AI?"

> I do — it's the foundation. It solved the *interface* problem: `IChatClient` is a genuinely
> good abstraction and I didn't reinvent it. What it doesn't do is let you *choose* an
> implementation from configuration. You still hand-write that factory in every project.
> ModelMux is that factory, done once.

### "What was the hardest part?"

> Streaming. Providers send token usage in a *trailing* update, not the first one, so cost
> tracking has to accumulate across the whole stream. And C# won't let you `yield return`
> inside a `try`/`catch`, so I had to drive the enumerator manually with the catch around
> `MoveNextAsync` and the yield outside it. It also has to record usage when the consumer
> abandons the stream early — those tokens were still billed.

### "Tell me about a bug you found."

> Two, and both are the kind that ship quietly.
>
> My unit tests passed while the library couldn't actually start. `PricingResolver` has two
> public constructors and the DI container couldn't choose. The tests built it by hand, so they
> never touched the container. Only running the sample app caught it. I added tests that build
> a real `ServiceProvider`.
>
> The second: costs displayed with a `₹` symbol when the values were USD, because .NET's `:C`
> format follows the machine locale. In a cost tool that's not cosmetic — it's a number people
> would act on.

### "How do you test something that calls an AI API?"

> Almost never call one. 152 tests use a fake `IChatClient` that reports which provider produced
> it, so I can assert routing with no network. They run in under two seconds and cost nothing.
> I have 5 live tests that skip themselves unless an API key is in the environment — CI has no
> keys, so it stays fast and can't fail because a vendor had an outage.

### "How would you let an LLM query a database safely?"

> Never let it write SQL. In my demo the model emits a structured intent restricted to an
> allowlist of reports, fields, and operators. The application validates every part of it and
> then executes with LINQ predicates chosen by a `switch` over already-validated values. No
> expression is ever built from model output. The model proposes; the application disposes.
> I have 19 adversarial tests for hallucinated fields, injected operators, and unparseable
> values.

### "What would you do differently?"

> I'd have run the sample app earlier. Both real bugs were invisible to unit tests and obvious
> the moment something actually executed. And I'd add fallback — right now a provider outage
> surfaces to the caller. It's classified and marked retryable, but nothing retries it yet.

---

## Part 6 — What to learn next

You now understand DI, decorators, options, `AsyncLocal`, async iterators, and the basics of
LLM APIs. Sensible next steps, in order:

| Topic | Why | Where |
|---|---|---|
| **Polly / resilience** | v0.2 needs retry + circuit breaker. The decorator hook is already there. | `Microsoft.Extensions.Resilience` |
| **OpenTelemetry** | Industry-standard tracing. `Microsoft.Extensions.AI` has `UseOpenTelemetry()` — read its source, it's a decorator like yours. | OTel .NET docs |
| **Embeddings & RAG** | The other half of AI engineering. Don't build a framework — use Semantic Kernel. | `IEmbeddingGenerator` |
| **Tool calling** | Letting a model call *your* functions. The natural sequel to structured output. | MEAI function invocation |
| **EF Core + a real store** | v0.5 persistence. You already know EF Core; this is applying it. | Your existing skill |

### The one habit worth keeping

Before building anything, spend 30 minutes checking whether it exists. In this project that
research produced two decisions that saved the most time:

- **Don't rebuild `IChatClient`** — Microsoft already owns it.
- **Gemini, Grok, and Ollama all speak the OpenAI protocol** — so one provider class covers
  four vendors plus every self-hosted runtime.

Neither of those was obvious from a blog post. Both came from reading current official docs.

---

## Quick reference

```bash
# Run the tests (no API key needed)
dotnet test

# See the routing table
dotnet run --project samples/ModelMux.Sample

# Run the web API
dotnet run --project samples/ModelMux.WebDemo

# Run live tests against a real provider
$env:GEMINI_API_KEY = "your-key"; dotnet test

# Fully local, no cloud
docker compose up --build
```

| Concept | File |
|---|---|
| Provider routing | [`ModelMuxRouter.cs`](../src/ModelMux.Core/ModelMuxRouter.cs) |
| Decorator pattern | [`CostTrackingChatClient.cs`](../src/ModelMux.Cost/CostTrackingChatClient.cs) |
| `AsyncLocal` | [`UsageScope.cs`](../src/ModelMux.Cost/Attribution/UsageScope.cs) |
| Async iterator + error handling | [`ErrorMappingChatClient.cs`](../src/ModelMux.Core/Errors/ErrorMappingChatClient.cs) |
| Safe LLM → query | [`IntentValidator.cs`](../samples/ModelMux.WebDemo/Reporting/IntentValidator.cs) |
| Money arithmetic | [`CostCalculator.cs`](../src/ModelMux.Cost/Pricing/CostCalculator.cs) |
| Testing without network | [`FakeProvider.cs`](../tests/ModelMux.Core.Tests/FakeProvider.cs) |
