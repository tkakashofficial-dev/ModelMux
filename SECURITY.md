# Security

## Reporting a vulnerability

Please report privately via GitHub's **Report a vulnerability** button on the Security tab,
rather than opening a public issue. I'll acknowledge within a few days.

## How ModelMux handles credentials

- **API keys are never logged.** They are read at client-construction time and handed to the
  provider SDK.
- **`ApiKeyEnvironmentVariable` is the recommended way** to supply a key, so credentials stay
  out of configuration files and therefore out of git.
- **A literal `ApiKey` field exists** for local development. It lands in `appsettings.json`, so
  treat any file containing one as a secret. The environment variable wins when both are set.
- Error messages name the *environment variable* that was empty — never its value.

## How ModelMux.Cost handles prompt content

- **Prompt and completion text is not recorded by default.** Prompts routinely contain personal
  data, so storing them is opt-in via `RecordPromptContent`.
- Enabling it makes your usage store a copy of everything sent to the model. Make sure you have
  a basis for that, and a retention policy.
- Token counts, cost, latency, and attribution are recorded always. None of them contain
  message content.

## What ModelMux does not do

It does not encrypt data at rest, filter prompts, redact PII, enforce data-residency, or
protect against prompt injection. It routes calls and records what they cost. Those other
concerns are real, and they are yours.

## Scope

Supported: the latest 0.x release. Given the version, expect fixes to ship as new releases
rather than patches to old ones.
