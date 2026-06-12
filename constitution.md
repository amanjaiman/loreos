# Lore — Project Constitution

> The persistent, immutable principles of the Lore (loreos) project. Every spec,
> plan, task, and pull request inherits these rules. When a spec conflicts with
> this document, this document wins. Amendments follow the process in §10.

Lore is the open-source **ambient capture agent for your personal memory layer**.
It watches what you do on your machine — locally, auditably, with aggressive
filtering — distills what matters, stores it in [mem0](https://github.com/mem0ai/mem0),
and serves it to every AI tool you use through MCP, a CLI, an agent skill, and a
documented REST API. Your models, your keys, your disk, your memory.

---

## 1. Principles

These are load-bearing. Code that violates a principle is wrong even if it works.

1. **Local by default, forever.** No account. No Lore-operated server in the data
   path. Everything binds to `127.0.0.1`.
2. **Zero telemetry.** Not opt-out — none. For software that reads your screen,
   any phone-home undermines the entire trust thesis. Any future usage insight is
   a separate, opt-in, documented, inspectable feature — never a default.
3. **Bring your own intelligence.** The user supplies an API key or a URL to a
   model they control. Lore never proxies inference and never ships a key.
4. **Readable over clever.** The code is the marketing. A contributor must be able
   to understand the capture pipeline in one sitting. Prefer the boring, obvious
   construction over the clever one.
5. **Standards over invention.** mem0 for memory, MCP for serving, the
   OpenAI-compatible API shape for model connectivity, Agent Skills (SKILL.md) for
   teaching tools. We adopt standards; we do not invent parallel ones.
6. **Meet every tool where it lives — as a shim, not a subsystem.** Every
   integration surface is a thin, stateless translation over the one behavior
   layer (the local API). A new surface adds zero business logic.

---

## 2. Tech Stack

The stack is fixed. Adding a new language or a heavy framework is a constitutional
amendment (§10), not a spec decision.

| Component | Stack | Notes |
|---|---|---|
| `agent` | C# / .NET 8 (`net8.0-windows`, `win-x64`, self-contained) | Capture pipeline + MCP server + local API host. `UseWPF=true` for UI Automation. |
| `cli` (`lore`) | C# / .NET 8 console (System.CommandLine) | Thin client of the local API. |
| `memoryd` | Python 3.11+, FastAPI, [mem0](https://github.com/mem0ai/mem0) OSS | Bundled sidecar; packaged with PyInstaller. Pinned mem0 version. |
| vector store | Qdrant (embedded / on-disk) | mem0's backend. No external service. |
| `app` | Electron + React 18 + TypeScript | Onboarding, library, settings. Talks only to the local API. |

Platform: **Windows first.** macOS/Linux capture are additive future modules and
must not be designed out of the architecture.

---

## 3. Architecture Invariants

1. **One behavior layer.** All product behavior lives behind the agent's local
   HTTP API on `127.0.0.1:7842`, backed by `IMemoryService`. The app, MCP server,
   CLI, and any future surface are clients of it. Behavior is never duplicated in
   a surface.
2. **One seam per external system.** mem0 is reached **only** through
   `IMemoryService` / `MemorydClient`. Model APIs **only** through
   `IInferenceBackend`. Win32 / UI Automation **only** through the capture
   extractor interfaces. Nothing else in the codebase makes outbound HTTP or P/Invoke calls.
3. **The agent is the supervisor.** It spawns and health-checks `memoryd`, and
   operates headless (no app required). The app is optional UI, never a dependency.
4. **Localhost only, always.** No surface binds to an external interface by
   default. The MCP server uses stdio or local Streamable HTTP. There is no remote
   listener.
5. **Dependency injection, no global state.** Services are constructor-injected via
   the .NET generic host. Config is loaded once and refreshed by a file watcher —
   never re-read on every property access. No static mutable state.

---

## 4. Security & Privacy Rules

These are non-negotiable and enforced in CI.

1. **No secrets in the repo, ever.** `gitleaks` runs on every PR. No API keys, no
   project URLs, no anon keys, no tokens — not even "public by design" ones. (The
   legacy `lore/v1` repo, which embedded Supabase credentials, is never published
   and never used as a git base. `loreos` starts from clean history.)
2. **User secrets live in the OS keystore.** API keys go in Windows Credential
   Manager (DPAPI), referenced from `config.json` by handle — never written to
   `config.json` or logged.
3. **The network-egress list is closed and documented.** The only outbound calls
   Lore may make are to the model endpoints the user explicitly configured.
   `docs/privacy.md` enumerates them and is kept in sync; a PR that adds an
   outbound call without updating it fails review.
4. **Capture is filtered before it is stored or sent.** The sensitivity filter
   chain (blocklist → UIA structural → regex) is trust-critical code and is held
   to the highest test bar (§6).

---

## 5. Coding Standards

- **Formatting & analyzers are gates, not suggestions.** `dotnet format
  --verify-no-changes` and analyzers-as-errors (`TreatWarningsAsErrors`) for C#;
  `ruff` + `mypy` for Python; `eslint` + `prettier` + `tsc --noEmit` for the app.
  A red linter is a red build.
- **Small, named, testable units.** No method does three jobs. Prefer pure
  functions for anything testable (filters, gating math, JSON shaping).
- **No dead code carried from v1.** Every file ported from `lore/v1` is re-read and
  re-justified. Commented-out blocks, beta TODOs, and dead branches do not come
  along. Porting is transcription with judgment, not copy-paste.
- **Errors are explicit.** No silently swallowed exceptions. Surface actionable
  messages, especially on the provider and memoryd seams.
- **Public surfaces are documented.** Every REST endpoint and MCP tool has a
  description; the local API publishes an OpenAPI spec.

---

## 6. Testing Standards

| Area | Bar |
|---|---|
| Sensitivity filter chain | ~100% line + branch coverage; exhaustive case table. This is the trust-critical path. |
| Smart-gating thresholds, config load/migration, JSON shaping | Unit-tested. |
| `memoryd` ↔ mem0 | Contract tests against the pinned mem0 version. |
| Provider backends | Tested against mocked HTTP; no live API calls in CI. |
| Overall agent | ≥ 70% line coverage. |

Tests are derived from a spec's acceptance criteria and written alongside the
code, not after. A task is not done until its tests are green.

---

## 7. The Delivery Gate

**Every change ships through `no-mistakes`.** No exceptions.

- Work is **committed on a feature branch** (never the default branch) before the
  gate runs — the gate validates committed history.
- The run is started with a complete `--intent`: the goal behind the change in the
  user's terms, plus the decisions and tradeoffs made. A thin intent makes the
  review flag deliberate choices as mistakes.
- The agent drives the gate (review → test → document → lint → push → PR → CI),
  resolves `auto-fix`/`no-op` findings on its judgment, and **escalates
  `ask-user` findings** to a human verbatim.
- A task/PR is "done" only at a `checks-passed` or `passed` outcome.

This gate is how the project keeps the "extremely clean" promise at scale. It is a
constitutional requirement, not a per-spec choice.

---

## 8. Integration Surfaces

Lore is reachable from every AI tool, but the cost of "compatible with everything"
is controlled by one rule (Principle 6): **a new standard gets a shim, not a
subsystem.** To be added, a surface must:

1. Be expressible purely as a translation onto the existing local API.
2. Ship with documentation and at least one contract test.
3. Carry a `surface:*` roadmap label so the community can own future ones.

Launch surfaces: **MCP** (stdio + Streamable HTTP), the **`lore` CLI**, a
**Claude Code Agent Skill**, and the **REST API + OpenAPI**. Everything else is a
labeled invitation.

---

## 9. Spec-Driven Development Workflow

All work flows through SDD. The artifacts:

- **`constitution.md`** — this file. Global, immutable-by-amendment.
- **`AGENTS.md`** — operational onboarding for any agent working a spec.
- **`specs/NNN-name/specification.md`** — intent: user stories, acceptance
  criteria, scope (in/out), non-functional requirements. *What and why,* not how.
- **`specs/NNN-name/plan.md`** — technical approach: architecture, data models,
  API contracts, dependencies, file-level layout.
- **`specs/NNN-name/tasks.md`** — atomic, dependency-ordered tasks, each scoped to
  a single PR (~1–4 hours), each ending at a green `no-mistakes` gate.

Specs are **living artifacts**: when implementation surfaces a better truth, the
spec is updated in the same PR, not left to rot. A subagent assigned a spec reads
the constitution and AGENTS.md first, then its three spec files, then works the
tasks in order.

---

## 10. Amendments

This document changes only by an explicit PR that (a) states the principle being
added/changed/removed, (b) justifies it, and (c) updates every spec the change
touches. Amendments go through the delivery gate like any other change. The intent
on that PR must name "constitution amendment" so the review reads it as deliberate.
