## ModelMux v0.1.0-preview.1

First preview. **Switch AI providers from configuration, not code.**

Your application depends on `IChatClient` and never on a vendor. Moving from Gemini to OpenAI
to a self-hosted GPU is an `appsettings.json` edit and a restart.

```jsonc
{
  "ModelMux": {
    "DefaultProfile": "fast",
    "Profiles": {
      "fast":    { "Provider": "Gemini", "Model": "gemini-2.5-flash", "ApiKeyEnvironmentVariable": "GEMINI_API_KEY" },
      "smart":   { "Provider": "OpenAI", "Model": "gpt-5-mini",       "ApiKeyEnvironmentVariable": "OPENAI_API_KEY" },
      "private": { "Provider": "Ollama", "Model": "llama3" }
    }
  }
}
```

```csharp
builder.Services.AddModelMux(builder.Configuration);
```

### What's in it

**Model profiles.** Name a model by the role it plays (`fast`, `smart`, `private`), not by who
sells it. Re-pointing a profile is a config change.

**Four providers, one implementation.** OpenAI, Google Gemini, xAI Grok, and Ollama all speak
the OpenAI chat-completions protocol, so they share one provider class and differ only in
endpoint. The useful consequence: vLLM, LM Studio, LocalAI, and any rented GPU work too — set
`Endpoint` and nothing else changes.

**Vendor-neutral errors.** Provider exceptions are classified into `AiErrorCategory`
(`RateLimit`, `Timeout`, `AuthenticationFailure`, …) with an `IsRetryable` flag. Write
`catch (ModelMuxProviderException ex) when (ex.IsRetryable)` without naming a vendor's exception
type. The original exception is always preserved as `InnerException`.

**Capability checks.** Declare what a model can do per profile; `RequireCapability` fails before
the network call rather than after a confusing provider error.

**Structured output.** `GetStructuredResponseAsync<T>()` returns typed objects, with the
capability check applied first.

**Cost tracking** (`ModelMux.Cost`, optional). Records tokens, cost, and latency per call,
attributed per tenant and per feature. Prices ship for 57 Anthropic, OpenAI and Gemini models,
each carrying a `LastVerified` date and a source URL. An unpriced model records `null` — never
`0` — and surfaces as `UnpricedCount`, so a total is never quietly understated.

### Demos

- **Console** — routing table, no API key needed
- **Web API** — `POST /api/chat`, SSE streaming, and a natural-language reporting endpoint
- **Docker Compose** — the whole thing against a local Ollama, no cloud

The reporting demo is worth a look: the model emits a **structured intent** restricted to an
allowlist, the application validates every field and operator, and only then executes with LINQ
predicates. The model never writes SQL. 19 adversarial tests cover hallucinated fields, injected
operators, and unparseable values.

### Quality

- **152 tests** passing, no network calls
- 5 live-provider tests that skip unless an API key is present
- 0 warnings, warnings-as-errors enabled
- Source Link and symbol packages included

### Known limitations

This is a **0.x preview** — the API may change before 1.0.

- **No fallback yet.** A provider outage surfaces to the caller, classified but not retried.
  Planned for v0.2; the decorator hook it will use already exists.
- **Live provider calls are not continuously verified.** CI has no API keys, so the tests that
  make real HTTP calls skip there.
- **Flat pricing only.** Gemini Pro models charge more above a 200k-token prompt; ModelMux
  records the standard tier, so cost for very large prompts is a lower bound.
- **In-memory usage store only.** Data is lost on restart.
- **Capabilities are declared, not verified.** Configure a lie and the provider will reject the
  request.
- **Only OpenAI-protocol providers ship.** Anthropic's native API and AWS Bedrock need a custom
  `IChatProvider`. The interface is public and documented for that.

### Positioning

ModelMux is built **on top of** `Microsoft.Extensions.AI`, not as a replacement for it.
`IChatClient` is a good abstraction and is used throughout. What ModelMux adds is the layer
above: choosing an implementation from configuration, several providers side by side, and
production concerns like error classification and cost attribution.

It's an experiment. Adoption and technical merit will decide whether it becomes more.

### Documentation

- [How it works](https://github.com/tkakashofficial-dev/ModelMux/blob/main/docs/how-it-works.md) — a walkthrough of every concept in the codebase
- [Architecture decisions](https://github.com/tkakashofficial-dev/ModelMux/blob/main/docs/architecture-decisions.md) — why it's shaped this way, and what is deliberately not built

### Not yet on NuGet

The `.nupkg` files are attached to this release. NuGet publication is coming.
