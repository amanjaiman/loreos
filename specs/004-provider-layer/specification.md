# 004 — Provider Layer (Bring Your Own Model) · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 002 (memoryd
> consumes the provider config). Parallelizable with 003.

## Overview

The provider layer is how Lore honors its core promise: **bring your own
intelligence.** Instead of v1's Lore-paid hosted proxy, the user supplies an API
key or a URL to a model they control, and that single choice drives both Lore's
capture-analysis calls and mem0's internals (via 002). This spec defines the
provider abstraction (`IInferenceBackend`), the four provider types, secret storage
in the OS keystore, and a "test connection" flow — and it **deletes** v1's hosted
proxy, GPU-gated in-process models, and backend-priority magic.

## User stories

- **As a user with an Anthropic (or OpenAI, or Gemini) key**, I paste it once and
  Lore works — for capture analysis and for memory — without an account.
- **As a local-first user**, I point Lore at my Ollama / LM Studio / vLLM endpoint
  by URL and use no cloud at all.
- **As a privacy-conscious user**, my API key is stored in the Windows keystore,
  never in a config file or a log, and I can confirm exactly which endpoint Lore
  calls.
- **As any user**, a "Test connection" button tells me immediately whether my model
  config works, with a clear error if it doesn't.

## Scope

### In scope

- **`IInferenceBackend`** abstraction (ported/cleaned from v1) and four backends:
  - `anthropic` — API key + model id (Claude; Haiku-class for cheap analysis).
  - `openai` — API key + model id.
  - `gemini` — API key + model id (port v1's `GeminiInferenceBackend`; it already
    takes a user key).
  - `openai_compatible` — **base URL** + optional key + model id (covers Ollama,
    LM Studio, llama.cpp server, vLLM, OpenRouter, Together, Groq — this single
    backend is the "direct URL to a hosted model" requirement).
- **Provider selection from config** — no priority heuristics; the config names the
  provider, and that is what is used.
- **Secret storage** — keys in Windows Credential Manager (DPAPI), referenced from
  `config.json` by handle (`api_key_ref`); never stored inline or logged.
- **Test-connection** — `POST /providers/test`: round-trip a tiny prompt, report
  model + latency, fail with actionable messages.
- **Provider→mem0 bridge** — translate the chosen provider into the config 002's
  `memoryd` needs (LLM + embedder), including the local-embedder default.
- **Deletions** — remove v1's `ProxyInferenceBackend`, `GeminiBatchService`,
  `OnnxGenAiBackend`, `HardwareDetector`, `ModelDownloader`, and the backend
  priority logic in `InferenceManager`.

### Out of scope

- The capture-analysis prompt and loop (003) — this spec provides the backend it
  calls.
- mem0 itself and memoryd routes (002) — this spec hands memoryd a config.
- The Settings/onboarding UI that collects the key (010) — this spec provides
  `/providers/test` and the config/credential plumbing it calls.

## Acceptance criteria

1. Each of the four provider types can run a capture-analysis prompt end-to-end
   (mocked HTTP in tests; manual live check documented).
2. `openai_compatible` works against a local Ollama endpoint by base URL with no
   cloud calls.
3. API keys are written to Windows Credential Manager and referenced by handle;
   `config.json` contains no key material; logs contain no key material (asserted).
4. `POST /providers/test` returns model + latency on success and a specific,
   actionable error on bad key / unreachable URL / unknown model.
5. The same provider config drives memoryd's mem0 LLM + embedder (002), with the
   local-embedder default when the provider has no embeddings.
6. v1's proxy, in-process ONNX, hardware detection, model downloader, and
   priority-based backend selection are absent from the codebase.

## Non-functional requirements

- **One seam:** all model calls go through `IInferenceBackend`; nothing else makes
  model HTTP calls (constitution §3.2).
- **No secrets at rest in plaintext** and **no secrets in logs** (constitution §4).
- **Egress honesty:** the only outbound calls are to the user's configured
  endpoint; `docs/privacy.md` is updated to list them.
- **Deterministic selection:** behavior is a pure function of config — no implicit
  fallbacks.

## Risks

- *OpenAI-compatible endpoints vary in conformance.* Mitigation: target the chat
  completions contract; surface clear errors; document tested endpoints.
- *Credential Manager interop edge cases.* Mitigation: isolate behind a small
  `ICredentialStore` with tests; fail closed with actionable guidance.
