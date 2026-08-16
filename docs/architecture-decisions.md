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

## 9. Errors are classified, never swallowed

**Decision.** Provider exceptions are mapped to `ModelMuxProviderException` with a vendor-neutral
`AiErrorCategory` and an `IsRetryable` flag. The original exception is always the inner exception.

**Why.** Retry logic shouldn't need to know whether OpenAI or Gemini is behind a profile —
`catch (ModelMuxProviderException ex) when (ex.IsRetryable)` should be enough. But hiding the
provider's own error would make real debugging impossible, so classification is additive.

**Consequence.** `Unknown` is never marked retryable. Guessing retryability on an unclassified
failure would invite infinite retry loops against something permanently broken.

**Caller cancellation is not mapped.** An `OperationCanceledException` raised because the
caller's token fired stays exactly that, or every `catch (OperationCanceledException)` in the
consuming application would silently stop working.

---

## 10. Capabilities are declared, not detected

**Decision.** `ModelCapabilities` defaults per provider and is overridable per profile. There is
no runtime probing.

**Why.** A hardcoded model→capability table would be wrong within weeks of the next model
launch, and probing costs a request and still can't be exhaustive. Declaring in configuration
puts the knowledge where it can be corrected without a package update.

**Consequence.** `Vision` defaults to false because it is far from universal; the rest default
to true because they're near-universal over the OpenAI protocol. `RequireCapability` throws
before any network call.

---

## 11. The model proposes, the application disposes

**Decision.** In the reporting demo, the model emits a `ReportIntent` constrained to an
allowlist of reports, fields, and operators. The application validates it, then executes it with
LINQ predicates chosen by a `switch` over already-validated values.

**Why.** A model that can emit SQL is a SQL-injection vector with a natural-language front end.
Constraining generation to a fixed vocabulary means the worst a hallucinated or adversarially
steered generation can produce is an intent the validator rejects — a 400, not an incident.

**Consequence.** No expression is ever built from model output. `EmployeeRepository.Execute`
re-validates and throws rather than trusting its caller, so forgetting to validate fails loudly
instead of quietly executing.

---

## 12. Deliberately not built

| Not built | Why |
|---|---|
| RAG, vector stores | Semantic Kernel covers this well |
| Agents, workflow engines | Different problem, crowded space |
| Custom embedding abstraction | `IEmbeddingGenerator` already exists |
| GPU orchestration | Out of scope; the `Endpoint` field is enough |
| A capability registry | Deferred until a concrete need shapes it |
| Benchmarks | ModelMux does not make inference faster; a benchmark would only measure routing overhead, which is not yet interesting |

---

## 13. Known limitations

- **No fallback yet.** A provider outage surfaces to the caller, classified but not retried.
  Planned for v0.2; the decorator hook it will use already exists.
- **Flat pricing only.** Gemini Pro models charge more above a 200k-token prompt; ModelMux
  records the standard tier, so cost for very large prompts is a lower bound. Noted in each
  affected entry's `Source`.
- **Live-provider tests skip by default.** They exist and run when an API key is present, but
  CI has none, so the HTTP round-trip against real vendors is not continuously verified.
- **In-memory usage store only.** Data is lost on restart. A persistent store is planned.
- **Capabilities are declared, not verified.** If configuration claims a model supports tool
  calling and it doesn't, ModelMux will let the request through and the provider will reject it.
- **Only OpenAI-protocol providers ship.** Anthropic's native API and AWS Bedrock need a custom
  `IChatProvider`. The interface is public and documented for exactly that.
- **API not stable.** 0.x — expect breaking changes before 1.0.
