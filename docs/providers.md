# Providers — bring your own intelligence

> Lore never ships a model or a key, and never proxies inference. You supply an
> API key or a URL to a model you control, and that **one** choice drives both
> capture analysis and memory (spec [004](../specs/004-provider-layer)). This
> document is the user-facing contract for the `provider` config block and the
> egress it implies; it is kept in sync with [`privacy.md`](privacy.md).

## The one choice

Configuration lives under `provider` in `config.json`:

```jsonc
"provider": {
  "type": "openai_compatible",                // anthropic | openai | gemini | openai_compatible
  "model": "qwen3:8b",                        // the model id to call
  "base_url": "http://localhost:11434/v1",    // required for openai_compatible; ignored otherwise
  "api_key_ref": "lore/provider",             // handle into the OS keystore; "" for keyless local
  "max_tokens": 1024                          // optional; upper bound on tokens per call (default: 1024)
}
```

Selection is a **pure function of `type`** — there is no priority chain, no GPU
gating, and no implicit default. A missing or malformed `provider` block fails
closed with an actionable message ("configure a provider", "set
provider.base_url", …) rather than silently picking something.

`api_key_ref` is a **handle**, not a key. The key itself lives in the Windows
Credential Manager (DPAPI) under that handle; it is never written to
`config.json` and never logged (see [Secrets](#secrets)).

## The four provider types

| `type` | Needs | `base_url` | Notes |
|---|---|---|---|
| `anthropic` | `api_key_ref`, `model` | — | Claude messages API. A Haiku-class model is a good, cheap default for capture analysis. |
| `openai` | `api_key_ref`, `model` | — | Chat completions. Also provides embeddings for memory. |
| `gemini` | `api_key_ref`, `model` | — | Google Gemini. For memory it is reached through Gemini's OpenAI-compatible endpoint. |
| `openai_compatible` | `model`, `base_url`; `api_key_ref` optional | required | Any OpenAI-compatible server: Ollama, LM Studio, llama.cpp server, vLLM, OpenRouter, Together, Groq. This is the "direct URL to a hosted model" path. |

### Examples

```jsonc
// Anthropic (Claude) — cheap, capable capture analysis
"provider": { "type": "anthropic", "model": "claude-haiku-4-5", "api_key_ref": "lore/provider" }

// OpenAI
"provider": { "type": "openai", "model": "gpt-4o-mini", "api_key_ref": "lore/provider" }

// Gemini
"provider": { "type": "gemini", "model": "gemini-2.5-flash", "api_key_ref": "lore/provider" }

// A hosted OpenAI-compatible gateway (keyed)
"provider": {
  "type": "openai_compatible",
  "model": "meta-llama/llama-3.1-8b-instruct",
  "base_url": "https://openrouter.ai/api/v1",
  "api_key_ref": "lore/provider"
}
```

## Local, no cloud: Ollama

For a fully local setup, point `openai_compatible` at a local server. With
[Ollama](https://ollama.com):

```bash
ollama serve                 # exposes an OpenAI-compatible API at http://localhost:11434/v1
ollama pull qwen3:8b         # a capable small chat model for capture analysis
ollama pull nomic-embed-text # the local embedding model memoryd uses by default
```

```jsonc
"provider": {
  "type": "openai_compatible",
  "model": "qwen3:8b",
  "base_url": "http://localhost:11434/v1",
  "api_key_ref": ""           // keyless: no key needed for a local endpoint
}
```

With a local provider **and** the default `memory.engine: "embedded"`, Lore makes
**zero non-loopback calls** — capture analysis, memory extraction, and embeddings
all stay on your machine. (LM Studio, llama.cpp's server, and vLLM work the same
way by their own `base_url`; set any `api_key_ref` if your server requires a key.)

## One provider, two consumers

The same `provider` block configures:

1. **Capture** — the `IInferenceBackend` behind the v2 pipeline's skeptical
   distiller ("what durable fact about the user does this episode support?") and
   its lifecycle arbitration ("does this new fact supersede that memory?").
2. **Memory** — `memoryd`'s embedder, applied via `POST /config` once memoryd is
   healthy. Since v2-001, memoryd stores facts verbatim (`infer=False`) — the
   chat model is configured but memoryd itself never calls it, and **recall makes
   zero generative calls** (one embedding per query).

### Minimum capable model

Distillation quality is strongly model-bound (measured in the v2-001 spike): a
~7B-class instruct model (e.g. `qwen2.5:7b-instruct`) or any hosted frontier
model distills clean, self-contained facts; a ~2B model confabulates and will
fill your memory with garbage. Point capture at the strongest model you're
comfortable running — the daily promotion budget caps spend, not quality.

The embedder follows the provider: when the provider has first-party embeddings
(OpenAI), memoryd uses them; otherwise it falls back to the **validated local
default** (Ollama `nomic-embed-text`), so a chat-only key never incurs surprise
embedding spend. Two mappings are worth knowing:

- **Gemini** has no native path in memoryd, so memory drives it through Gemini's
  OpenAI-compatible endpoint (`…/v1beta/openai/`). Capture uses the native Gemini
  API directly.
- **Keyless `openai_compatible`** is mapped onto memoryd's native Ollama path (the
  `/v1` suffix is stripped). If your keyless server is *not* Ollama (e.g. a keyless
  LM Studio), give it any non-empty `api_key_ref` so memory uses the
  OpenAI-compatible path instead.

## Secrets

Keys are stored in the **Windows Credential Manager (DPAPI)**, referenced from
`config.json` by handle (`api_key_ref`). They are:

- never written to `config.json` (a one-time migration lifts any inline v1 key
  into the keystore and strips it),
- never written to `lore.log` (a redaction filter scrubs any registered secret
  from every log line, as defense in depth on top of code that never logs keys),
- sent only to the endpoint you configured — over loopback for a local provider.

## Test connection

`POST /providers/test` validates a config before you rely on it. It builds the
selected backend, sends a tiny prompt, and returns:

```jsonc
{ "ok": true, "model": "gpt-4o-mini", "latency_ms": 412 }
```

or, on failure, a specific and actionable error:

```jsonc
{ "ok": false, "error_kind": "Unauthorized",   "error": "OpenAI rejected the API key (HTTP 401). Check the key for this provider." }
{ "ok": false, "error_kind": "Unreachable",    "error": "Could not reach localhost: connection refused" }
{ "ok": false, "error_kind": "ModelNotFound",  "error": "Anthropic does not recognize model 'nope' (HTTP 404). Check provider.model." }
{ "ok": false, "error_kind": "configuration",  "error": "Provider 'openai_compatible' requires a base URL. Set provider.base_url …" }
```

The app (settings/onboarding) calls this so you get an immediate, clear answer.

## Live check (manual)

Automated tests cover every backend against mocked HTTP. To confirm a real
provider end-to-end:

1. Store the key under the handle (e.g. via the settings UI, which writes to
   Credential Manager).
2. Call `POST /providers/test` with that `provider` config and confirm
   `ok: true` with a sane `latency_ms`.
3. Let the capture loop run and confirm an observation lands in memory.

For `openai_compatible` against a local Ollama (acceptance criterion 2): start
`ollama serve`, point `base_url` at `http://localhost:11434/v1`, and confirm the
test succeeds with **no** outbound (non-loopback) network traffic.

## Tested endpoints

| Provider | Endpoint | Auth |
|---|---|---|
| Anthropic | `https://api.anthropic.com/v1/messages` | `x-api-key` header |
| OpenAI | `https://api.openai.com/v1/chat/completions` | `Authorization: Bearer` |
| Gemini | `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent` | `x-goog-api-key` header |
| openai_compatible | `{base_url}/chat/completions` | `Authorization: Bearer` (omitted if keyless) |
| Ollama (local, verified) | `http://localhost:11434/v1/chat/completions` | none |
