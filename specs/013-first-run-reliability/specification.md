# 013 — First-Run Capture Reliability · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 002 (memoryd /
> mem0 config), 003 (capture pipeline), 004 (provider→mem0 bridge), 011 (packaged
> install). **Fixes** defects found on the first packaged install.

## Overview

When Lore was installed from the packaged installer and onboarded by a **cloud-only
user** (a Gemini key, no local model server) for the first time, **nothing was
captured and nothing was stored** — despite every per-spec unit test being green. Three
independent defects, none catchable by the existing suite, sat on the one path no spec
owned end-to-end: *the first run of the packaged app on a clean machine.*

1. **Capture crashed on every tick.** `Win32ForegroundWindowSource` P/Invoked
   `GetWindowTextLength`, but `[LibraryImport]` binds the exact name and `user32.dll`
   exports only `GetWindowTextLengthW/A`. Every window poll threw
   `EntryPointNotFoundException`, swallowed as "capture tick failed; continuing,"
   producing zero observations. Capture (003) was tested against a mock window source,
   so the real Win32 call was never exercised.

2. **Onboarding didn't take effect until a restart.** The inference backend and memoryd
   were configured **once at startup**; `PATCH /config` (the onboarding save) only wrote
   `config.json` and never reconfigured the running agent. On a fresh install the user
   always onboards *after* first launch, so capture analysis stayed disabled and memoryd
   unconfigured until the app was fully quit and relaunched.

3. **Memory storage failed for every provider without first-party embeddings.** memoryd
   needs an **embedder** (to vectorize memories) that is separate from the chat LLM. The
   `follow_provider` policy uses the provider's own embeddings only when
   `provider.type == "openai"` *exactly*, and otherwise falls through to a **local Ollama
   default** at `localhost:11434`. So a cloud-only user with no Ollama gets HTTP 400
   "failed to initialize memory engine." This is **not** Gemini-specific — it breaks
   every provider that isn't literal OpenAI: Gemini, Anthropic (which has **no**
   embeddings API at all), OpenRouter/Groq (chat-only), and any self-hosted endpoint
   without an embedding model loaded.

Defects 1 and 2 are agent-only and were fixed and verified in the session that found
them (recorded here for traceability — see [`tasks.md`](tasks.md)). Defect 3 needs a
coherent **embedder policy that spans all providers and self-hosted setups**, plus the
**regression gate** — a packaged-install smoke test for the cloud-only first run —
whose absence is the structural reason all three escaped.

### Embedder policy (the decision this spec encodes)

Embeddings come from the provider or the user's own endpoint where available; where they
genuinely aren't, Lore **requires an explicitly configured embedder** and says so
clearly — it does **not** silently assume a local Ollama, and it does **not** bundle an
embedding model. Concretely:

| Provider / setup | Embedder | First run |
|---|---|---|
| `openai` | provider's `text-embedding-3-small` | zero-config ✅ |
| `gemini` | provider's `text-embedding-004` via its OpenAI-compat endpoint | zero-config ✅ |
| `openai_compatible`, self-hosted **with** an embed model (Ollama/LM Studio/vLLM serving embeddings) | that same endpoint | zero-config ✅ |
| `openai_compatible`, keyed cloud **with** embeddings (Together, Fireworks, …) | that endpoint, embed model **named by the user** | works once embedder model set |
| `anthropic` (no embeddings API), `openai_compatible` chat-only (OpenRouter, Groq), self-hosted **without** an embed model | **user-configured embedder** | actionable error until configured |

The escape hatch — an explicit `embedder` block in Lore's config, forwarded to memoryd —
is what makes "else require config" real, and it must work end-to-end for **any**
provider.

## User stories

- **As a Gemini-only (or OpenAI-only) user**, I install Lore, paste my key, and capture
  works **immediately** — observations are analyzed *and* stored — with no Ollama and no
  restart.
- **As a self-hosted user** (Ollama / LM Studio / vLLM) already serving an embedding
  model, my one endpoint drives chat *and* embeddings with no extra setup.
- **As a user of a provider without embeddings** (Anthropic; OpenRouter/Groq), Lore tells
  me plainly that I need to point it at an embeddings endpoint and exactly which config to
  set — instead of failing with an opaque error or silently requiring software I don't
  have.
- **As any user**, I can configure an explicit embedder (an OpenAI-compatible or Ollama
  embeddings endpoint, with model + dimension) and it is used for memory, independent of
  my chat provider.
- **As a maintainer**, a release is gated by a smoke test that installs the packaged app
  on a clean (no-Ollama) environment, onboards with a cloud key, and asserts a memory
  persists — so a regression in any of these areas fails the build, not the user.

## Scope

### In scope

- **First-party embeddings where the provider has them, zero-config.** The provider→mem0
  bridge (004) emits an **explicit** embedder for providers whose embeddings Lore knows:
  `openai` (confirm the existing path) and `gemini` (its OpenAI-compatible embeddings
  endpoint). One key drives chat LLM **and** embeddings; no Ollama.
- **Self-hosted embeddings via the user's own endpoint.** For `openai_compatible`, the
  embedder targets the **provider's own `base_url`** (reusing its key when keyed), not a
  hard-coded `localhost`, so a self-hosted endpoint that serves embeddings is used
  directly.
- **Explicit user-configured embedder, end-to-end.** A `embedder` block in Lore's config
  (type / model / base_url / dims, key reused from the provider where applicable) is
  forwarded from the agent through to memoryd and **overrides** the defaults. This is the
  supported path for providers without first-party embeddings and for keyed compatibles
  whose embed model id Lore can't guess.
- **Actionable errors, never a silent localhost assumption.** When no embedder can be
  resolved (provider has none and none configured, or a configured/​self-hosted endpoint
  is unreachable), memoryd's `POST /config` returns an error naming the provider, what is
  missing, the endpoint/model tried, and **exactly which config keys to set** — with no
  key material. The implicit local-Ollama fallback is removed.
- **Capture P/Invoke fix** (defect 1) and **live provider reconfigure on `PATCH /config`**
  (defect 2). *Already landed this session; tracked here for SDD traceability.*
- **Packaged-install first-run smoke test.** Exercises the cloud-only first run against a
  **real** memoryd with **no Ollama present**, asserting a stored memory — and asserting
  that a no-embeddings provider returns the actionable error rather than crashing.

### Out of scope

- **Bundling an embedding model** with the installer (the rejected "always-local"
  option). Lore ships no embedder; it uses the provider's, the user's endpoint, or a
  user-configured one.
- **A memoryd-native Gemini provider type.** Gemini keeps riding `openai_compatible` for
  the LLM; this spec only corrects the **embedder**.
- **Auto-detecting whether an arbitrary `openai_compatible` endpoint supports
  embeddings or guessing its embed model id.** Unknown endpoints require an explicit
  embedder; Lore does not probe or guess.
- **Onboarding UI for the embedder field** (010). The agent/config plumbing accepts an
  explicit embedder; surfacing it in Settings is a follow-on. (Docs cover the config-file
  path in the interim.)
- **Changing the capture prompt/loop** (003) beyond the two fixes above.

## Acceptance criteria

1. **Known-embeddings providers store memories with no Ollama.** On a machine with **no**
   Ollama, both `openai` and `gemini` configs initialize the memoryd engine and a
   captured observation is stored and retrievable via `GET /memories`. (The exact field
   failure now passes.)
2. **One key, both jobs, for known providers.** With only a Gemini (or OpenAI) key, capture
   analysis and memory embeddings both succeed on that single key; no second endpoint
   required.
3. **Self-hosted endpoint embeddings work and aren't pinned to localhost.** An
   `openai_compatible` provider serving embeddings at its own `base_url` is used for the
   embedder at *that* URL; configuration and storage succeed with no `localhost`
   assumption.
4. **Explicit embedder is honored end-to-end.** With a provider that has no first-party
   embeddings (e.g. Anthropic) plus an explicit `embedder` block pointing at a reachable
   embeddings endpoint, memoryd configures and stores using that embedder.
5. **No-embeddings providers fail with an actionable, key-free message.** With such a
   provider and **no** embedder configured, `POST /config` returns an error naming the
   provider, the missing embedder, and the exact config keys to set — not "failed to
   initialize memory engine," and never containing key material.
6. **Capture runs without the P/Invoke crash** (regression test for defect 1).
7. **Onboarding takes effect live** — saving a provider via `PATCH /config` against a
   running agent enables analysis and configures memoryd with no restart (regression test
   for defect 2).
8. **First-run smoke test gates releases.** A documented, automatable smoke test runs the
   cloud-only first run against a real memoryd with no Ollama and asserts a stored memory,
   plus the actionable-error path; it fails if criteria 1/3/4/5/6/7 regress.

## Non-functional requirements

- **Zero-egress preserved.** No new outbound *destination*: provider embeddings go to the
  **same** host the user already authorized for the LLM; self-hosted/explicit embedders go
  where the user pointed them. No call is added that the user didn't configure
  (constitution §1, §4.3). `docs/privacy.md` is reviewed and, if it implies cloud
  providers make no embedding calls, corrected — without adding an egress-table row for a
  host already listed.
- **No secrets in logs or errors.** The provider key reaches memoryd only over loopback,
  is never logged, and the new actionable errors name URLs and models but **never** key
  material (constitution §4).
- **Deterministic, no hidden machine dependency.** The embedder is a pure function of the
  provider config and any explicit `embedder` block — never an implicit dependency on
  undocumented local state (the exact failure this fixes).

## Risks

- **Embedding model ids and dimensions per endpoint.** Each embeddings endpoint names its
  model differently and emits a specific vector dimension; Qdrant's collection must match.
  *Mitigation:* pin the known ones (OpenAI `text-embedding-3-small` = 1536; Gemini
  `text-embedding-004` = 768 — confirm against the live endpoint), require `dims` on an
  explicit embedder, and extend memoryd's known-dimensions map.
- **Dimension mismatch on an existing store.** A store created under one embedder can't be
  reused by another of different dimension. *Mitigation:* this is a first-run fix (fresh
  installs have no store; the broken installs never initialized one). Fail clearly on a
  genuine mismatch rather than corrupting the store.
- **Keyed `openai_compatible` embed model is unguessable.** Together/Fireworks support
  embeddings but with provider-specific model ids. *Mitigation:* require the user to name
  the embed model (explicit embedder, reusing base_url + key); don't guess.
- **Frozen memoryd rebuild.** The memoryd change only reaches users via a rebuilt
  PyInstaller binary in a new release; it can't be hot-swapped like the agent fixes.
  *Mitigation:* fold into the next release; the smoke test runs against the packaged
  artifact so the fix is verified as shipped.
- **Live smoke test needs a real key.** *Mitigation:* gate the live leg on a CI-secret
  key (skip-with-warning when absent); cover wiring deterministically with a stub embedder
  so the non-live check still guards the regression.

## Human-gated

- Cutting the **release** that ships the rebuilt memoryd (a tagged build — see 012).
- Providing a **provider API key as a CI secret** for the live smoke leg (or running it
  locally); without it the live leg is skipped, not failed.
