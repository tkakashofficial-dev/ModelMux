# Contributing

Thanks for looking. ModelMux is early (0.x), so the API can still change — which makes this a
good time to argue about the design.

## Getting started

```bash
git clone https://github.com/tkakashofficial-dev/ModelMux.git
cd ModelMux
dotnet build
dotnet test
```

You need the **.NET 10 SDK**. No API keys: every test uses a fake provider and touches no
network.

## Before opening a pull request

```bash
dotnet build -c Release   # warnings are errors
dotnet test -c Release
```

## What makes a change easy to accept

- **A test that fails without your change.** Especially for bug fixes.
- **Public API gets XML docs.** `GenerateDocumentationFile` is on and CS1591 is an error, so
  the build enforces this.
- **Say why in the code, not what.** Comments should explain a decision that isn't obvious
  from reading the lines below them.
- **New provider?** Implement `IChatProvider`. If the service speaks the OpenAI protocol, you
  probably only need a new entry in `KnownProviders` rather than a new class.
- **New pricing entry?** It must carry a `LastVerified` date and a `Source` URL pointing at the
  provider's published pricing. Unverified prices are not accepted — a wrong price in a cost
  tool is worse than a missing one, because it is reported with the same confidence as a
  correct one.

## What is intentionally out of scope

See [`docs/architecture-decisions.md`](docs/architecture-decisions.md) — RAG, agents, vector
stores, and GPU orchestration are deliberately not part of this project. An issue proposing
them will likely be closed with a pointer to that document, which is not a comment on the idea.

## Reporting bugs

Include the .NET version, ModelMux version, your profile configuration **with secrets
removed**, and what you expected versus what happened.
