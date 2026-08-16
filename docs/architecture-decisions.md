# Architecture decisions

Why ModelMux is shaped the way it is, and — more usefully — what it deliberately does not do.

---

## 1. Build on `Microsoft.Extensions.AI`, don't replace it

**Decision.** ModelMux does not define its own chat abstraction. Providers return
`IChatClient`, the interface from `Microsoft.Extensions.AI`.

**Why.** `Microsoft.Extensions.AI.Abstractions` has ~81M downloads and is the de facto
standard. Everything built for it — middleware, function calling, evaluation, OpenTelemetry —
works with any `IChatClient`. A parallel `IModelMuxClient` would cut users off from all of it
in exchange for nothing.

**Consequence.** ModelMux is additive. An application already using `IChatClient` adopts it by
changing registration only.

---

## 2. What ModelMux adds

`Microsoft.Extensions.AI` gives you the abstraction. It does not give you a way to *choose* an
implementation from configuration — you write that factory by hand in every project. ModelMux
is that factory, plus the indirection that makes it useful:

| Capability | `Microsoft.Extensions.AI` | ModelMux |
|---|:---:|:---:|
| `IChatClient` abstraction | ✅ | uses it |
| Provider selected from `appsettings.json` | ❌ | ✅ |
| Several providers live in one app | ❌ | ✅ |
| Logical profiles instead of vendor names | ❌ | ✅ |
| Repoint at a self-hosted endpoint via config | ❌ | ✅ |

---

## 3. Model profiles, not provider names

**Decision.** Application code names a *profile* — `fast`, `smart`, `private` — never a vendor.

**Why.** The thing that changes over a product's life is which vendor is cheapest, fastest, or
permitted. What doesn't change is the *role* the model plays. Naming the role gives one place
to change when the vendor does.

**Consequence.** `"fast"` can be Gemini today and a self-hosted Llama next quarter with no code
change and no redeploy of business logic.

---

## 4. One OpenAI-compatible provider instead of three SDKs

**Decision.** OpenAI, Gemini, and Ollama are served by a single `OpenAICompatibleProvider`
differing only in endpoint.

**Why.** Gemini and Ollama both publish OpenAI-compatible endpoints
([Gemini](https://ai.google.dev/gemini-api/docs/openai),
[Ollama](https://docs.ollama.com/openai)). Three SDK integrations would mean three
dependency chains and three sets of bugs for one wire format.

**Consequence — the valuable one.** Anything else speaking that protocol works with no new
code: vLLM, LM Studio, LocalAI, Groq, OpenRouter, a rented GPU. Self-hosting is an `Endpoint`
string, which is the whole "move to our own GPU later" story handled on day one.

**Limits.** Providers with a genuinely different wire format — Anthropic's native API, AWS
Bedrock — need their own `IChatProvider`. The interface exists for exactly that.

---

## 5. Decorators as the extension point

**Decision.** `IChatClientDecorator` wraps every client the router creates.

**Why.** Cost tracking, fallback, and caching are all "wrap the client and add behaviour". One
hook serves all three, so providers never learn about cross-cutting concerns.

**Consequence.** Decorators apply in registration order, last registered outermost. Cost
tracking registers last so its recorded duration includes anything inside it.

---

## 6. Two packages, one repository

**Decision.** `ModelMux` (routing) and `ModelMux.Cost` (usage and cost) ship separately from
one repo.

**Why.** Someone who wants provider switching shouldn't download a pricing table for 57 models.
Separate *repos* would mean two CI pipelines and two issue trackers for one product.

**Consequence.** `ModelMux.Cost` depends on `ModelMux`, never the reverse — enforced by a test
asserting routing works with the cost package absent.

---

## 7. Fail at startup, not at first request

**Decision.** Missing default profile, unknown provider, or absent credential throws when the
router is constructed.

**Why.** A configuration error found during deployment is cheap. The same error found by a
customer at 3am is not.

**Consequence.** Messages name the offending key and the fix:

> Profile 'fast' uses provider 'Gemini', which requires an API key, but none was found. Set
> `ModelMux:Profiles:fast:ApiKeyEnvironmentVariable` to the name of an environment variable
> holding the key…

---

## 8. Cost is stored, never recomputed

**Decision.** `UsageRecord.Cost` is computed at call time and persisted.

**Why.** Provider prices change. A record recomputed at read time would silently restate
history every time a price moved.

**Related.** An unpriced model records `null`, never `0` — and surfaces as
`UsageSummary.UnpricedCount`, so a total is never quietly understated. Only prices verified
against a provider's published page ship, each with a `LastVerified` date and source URL.

---

## 9. Deliberately not built

| Not built | Why |
|---|---|
| RAG, vector stores | Semantic Kernel covers this well |
| Agents, workflow engines | Different problem, crowded space |
| Custom embedding abstraction | `IEmbeddingGenerator` already exists |
| GPU orchestration | Out of scope; the `Endpoint` field is enough |
| A capability registry | Deferred until a concrete need shapes it |
| Benchmarks | ModelMux does not make inference faster; a benchmark would only measure routing overhead, which is not yet interesting |

---

## 10. Known limitations

- **No fallback yet.** A provider outage surfaces to the caller. Planned for v0.2.
- **Flat pricing only.** Gemini Pro models charge more above a 200k-token prompt; ModelMux
  records the standard tier, so cost for very large prompts is a lower bound. Noted in each
  affected entry's `Source`.
- **No live-API tests.** Every test uses a fake provider. The wiring is proven; the HTTP
  round-trip against real vendors is not.
- **In-memory usage store only.** Data is lost on restart. A persistent store is planned.
- **API not stable.** 0.x — expect breaking changes before 1.0.
