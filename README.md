# ModelMux

**Switch AI providers from configuration, not code.**

ModelMux maps logical model profiles — `fast`, `smart`, `private` — onto OpenAI, Google
Gemini, Ollama, or any OpenAI-compatible endpoint. Your application depends on `IChatClient`
and never on a vendor.

```csharp
// Your code. It never names a provider, and never changes.
public class ReportService(IChatClient ai)
{
    public Task<ChatResponse> SummariseAsync(string data) =>
        ai.GetResponseAsync([new ChatMessage(ChatRole.User, $"Summarise: {data}")]);
}
```

```jsonc
// Your configuration. This is the only thing you change.
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
builder.Services.AddModelMux(builder.Configuration);   // that's the whole setup
```

---

## Why

`Microsoft.Extensions.AI` already gives .NET a shared abstraction — `IChatClient` — and
ModelMux does **not** replace it. It sits on top and fills two gaps:

| | Microsoft.Extensions.AI | ModelMux |
|---|:---:|:---:|
| Common `IChatClient` abstraction | ✅ | uses it |
| Streaming, tools, middleware | ✅ | uses it |
| Pick a provider from `appsettings.json` | ❌ | ✅ |
| Several providers side by side in one app | ❌ | ✅ |
| Logical profiles instead of vendor names | ❌ | ✅ |
| Repoint at a self-hosted GPU without code changes | ❌ | ✅ |

Out of the box, wiring a provider means writing a factory by hand in every project. ModelMux
is that factory, done once, driven by config.

## Providers

Most model servers now speak the OpenAI chat-completions protocol, so one implementation
covers a lot of ground:

| Provider | Endpoint | API key |
|---|---|---|
| `OpenAI` | SDK default | required |
| `Gemini` | `generativelanguage.googleapis.com/v1beta/openai/` | required |
| `Ollama` | `localhost:11434/v1/` | not required |
| *anything OpenAI-compatible* | set `Endpoint` | your choice |

That last row is the important one. vLLM, LM Studio, LocalAI, a rented GPU, or a gateway —
point a profile's `Endpoint` at it and nothing else changes:

```jsonc
"gpu": {
  "Provider": "OpenAI",
  "Model": "llama-3-70b",
  "Endpoint": "http://your-gpu-box:8000/v1/"
}
```

Providers with a genuinely different wire format get their own `IChatProvider`:

```csharp
builder.Services.AddModelMux(builder.Configuration)
    .AddProvider(new MyAnthropicProvider());
```

## Choosing a profile at runtime

Most code should just inject `IChatClient` and stay unaware profiles exist. When a component
genuinely needs to choose:

```csharp
public class ReportService(IModelMux mux)
{
    public Task<ChatResponse> DraftAsync(string data) =>
        mux.GetClient("fast").GetResponseAsync(...);      // cheap

    public Task<ChatResponse> AuditAsync(string data) =>
        mux.GetClient("smart").GetResponseAsync(...);     // expensive, worth it
}
```

## API keys

Use `ApiKeyEnvironmentVariable` and keep credentials out of config files:

```jsonc
{ "Provider": "OpenAI", "Model": "gpt-5-mini", "ApiKeyEnvironmentVariable": "OPENAI_API_KEY" }
```

A literal `ApiKey` field exists for local spikes. It lands in `appsettings.json` and therefore
in git — the environment variable wins when both are set.

## Try it

```bash
git clone <this repo>
cd ModelMux
dotnet run --project samples/ModelMux.Sample
```

```
  PROFILE     PROVIDER    MODEL                     ENDPOINT
------------------------------------------------------------------------------
  fast *      Gemini      gemini-2.5-flash          (provider default)
  private     Ollama      llama3                    (provider default)
  selfhosted  OpenAI      llama-3-70b               http://localhost:8000/v1/
  smart       OpenAI      gpt-5-mini                (provider default)
```

Change a `Provider` in `samples/ModelMux.Sample/appsettings.json`, run again, and note that
`ReportService` was not touched.

## Design decisions

**Configuration is validated at startup, not at first request.** A missing default profile or
an unregistered provider throws immediately, and the message names the exact config key and
what to set it to.

**Clients are created once per profile and cached.** Provider clients own HTTP connection
pools; building one per request is how you exhaust sockets.

**`IChatClient` is registered for the default profile.** Code that already injects
`IChatClient` keeps working with no change.

**No new chat abstraction.** `Microsoft.Extensions.AI` is the ecosystem standard and
everything downstream already speaks it. Inventing a parallel interface would cut you off from
that ecosystem for no benefit.

## Status

**v0.1 — early.** The core works and is tested, but the API may change before 1.0.

```bash
dotnet test    # 26 passing, no network calls
```

## Roadmap

- [x] **v0.1** — profiles, config-driven switching, OpenAI/Gemini/Ollama, self-hosted endpoints
- [ ] **v0.2** — fallback: if the primary provider fails, try the next
- [ ] **v0.3** — cost and token tracking per profile
- [ ] **v0.4** — response caching

## License

MIT
