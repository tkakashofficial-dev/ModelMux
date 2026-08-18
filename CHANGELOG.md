# Changelog

Notable changes to ModelMux. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

While on `0.x`, the public API may change between releases.

## [Unreleased]

### Planned

- Fallback between providers when the primary fails
- Retry, timeout and circuit breaker via `Microsoft.Extensions.Resilience`
- OpenTelemetry tracing and metrics
- Persistent usage store, so data survives a restart
- Response caching

## [0.1.0-preview.2] — 2026-08-18

### Added

- Package icon, shown on nuget.org.
- Logo in the README, switching automatically between GitHub's light and dark themes.
- NuGet and license badges.

### Changed

- Installation instructions now include `--prerelease`, which is required until 1.0.

### Notes

No functional changes. Both packages are byte-identical in behaviour to
`0.1.0-preview.1`; this release exists because a published NuGet version can never be
replaced, so adding the icon required a new one.

## [0.1.0-preview.1] — 2026-08-16

First public release. Two packages: `ModelMux` and `ModelMux.Cost`.

### Added — ModelMux

- **Model profiles.** Named configuration entries mapping a logical name to a provider and
  model, resolved from `IConfiguration`. Names are arbitrary application-chosen strings.
- **Provider routing.** `IModelMux.GetClient(profileName)` resolves a profile to a live
  `IChatClient`. `IChatClient` is also registered directly for the default profile, so existing
  code that injects it continues to work unchanged.
- **Providers.** OpenAI, Google Gemini, xAI Grok and Ollama, served by a single
  `OpenAICompatibleProvider` differing only in endpoint. Any other OpenAI-protocol endpoint —
  vLLM, LM Studio, LocalAI, a self-hosted deployment — is reached by setting `Endpoint`.
- **`IChatProvider`.** Public extension point for providers with a different wire format.
  Registering one whose `Name` matches a built-in replaces it.
- **`IChatClientDecorator`.** Extension point for cross-cutting behaviour. Decorators apply in
  registration order, last registered outermost.
- **Error classification.** Provider exceptions are mapped to `ModelMuxProviderException` with
  a vendor-neutral `AiErrorCategory` and an `IsRetryable` flag. The original exception is
  always preserved as the inner exception. Caller cancellation is deliberately not translated.
- **Capabilities.** `ModelCapabilities` declares per profile what a model supports;
  `RequireCapability` throws before any network call.
- **Structured output.** `GetStructuredResponseAsync<T>()`, with the capability check applied
  first.
- **Startup validation.** Missing default profile, unknown provider, absent credential and
  malformed endpoint all fail when the router is constructed, with messages naming the exact
  configuration key at fault.
- **Client caching.** One client per profile, built once under
  `LazyThreadSafetyMode.ExecutionAndPublication`, disposed with the router.

### Added — ModelMux.Cost

- **Usage recording.** Tokens, cost, latency, model, provider and outcome captured per call,
  for both blocking and streaming paths — including streams a consumer abandons early.
- **Attribution.** `UsageScope` flows tenant, feature and user across `await` boundaries via
  `AsyncLocal`. Falls back to the profile name when no scope is open. Replaceable via
  `IUsageAttributionAccessor` for applications that already resolve a tenant.
- **Pricing.** 57 models across Anthropic, OpenAI and Google, each carrying a `LastVerified`
  date and a source URL. Exact match first, then longest-prefix so date-suffixed model ids
  resolve. Overridable from configuration.
- **Unpriced models record `null`, never `0`,** and surface as `UsageSummary.UnpricedCount`.
- **Estimated token counts are flagged** via `IsEstimated` when a provider reports none.
- **Prompt content is not recorded by default.** Opt in via `RecordPromptContent`.
- **Store failures are logged and swallowed** — telemetry cannot break the call it measures.

### Added — repository

- ASP.NET Core sample with chat, SSE streaming, a natural-language reporting endpoint, and a
  usage summary endpoint.
- Console sample that runs with no API key.
- Dockerfile and a Compose stack running against a local Ollama.
- CI on every push; NuGet publishing via GitHub OIDC trusted publishing, with no stored
  credential.
- 164 tests. Five live-provider tests skip unless the matching API key is present.

### Known limitations

- No fallback or retry. A provider outage surfaces to the caller, classified but not retried.
- Switching providers requires an application restart; profiles are resolved at construction.
- Usage is stored in memory only and is lost on restart.
- Capabilities are declared, not verified — a configuration that lies will be believed.
- Gemini Pro models are priced by prompt size; the standard tier is recorded, so cost for very
  large prompts is a lower bound.
- Only OpenAI-protocol providers ship. Anthropic's native API and AWS Bedrock need a custom
  `IChatProvider`.

[Unreleased]: https://github.com/tkakashofficial-dev/ModelMux/compare/v0.1.0-preview.2...HEAD
[0.1.0-preview.2]: https://github.com/tkakashofficial-dev/ModelMux/releases/tag/v0.1.0-preview.2
[0.1.0-preview.1]: https://github.com/tkakashofficial-dev/ModelMux/releases/tag/v0.1.0-preview.1
