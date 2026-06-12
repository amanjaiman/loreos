# 004 — Provider Layer · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Reuse v1's sound `IInferenceBackend` abstraction; replace its product-shaped
selection logic (hosted-first priority, GPU gating) with **config-driven
selection** across four backends. Add OS-keystore secret storage and a test-connection
endpoint. Bridge the chosen provider into memoryd's mem0 config so the user
configures a model **once**.

## Structure

```
agent/Providers/
├── IInferenceBackend.cs        # GenerateAsync(prompt, maxTokens) → InferenceResult  (ported)
├── InferenceResult.cs          # (ported)
├── ProviderSelector.cs         # config → the one backend to use (no priority magic)
├── AnthropicBackend.cs         # NEW — Claude messages API
├── OpenAiBackend.cs            # NEW — chat completions
├── GeminiBackend.cs            # ported & cleaned from v1 GeminiInferenceBackend
├── OpenAiCompatibleBackend.cs  # NEW — base-URL chat completions (Ollama/LM Studio/vLLM/...)
├── ProviderConfig.cs           # typed view over the config "provider" block
└── Mem0ProviderBridge.cs       # provider → memoryd /config payload (+ local embedder default)
agent/Config/
└── CredentialStore.cs          # ICredentialStore over Windows Credential Manager (DPAPI)
agent/Api/Endpoints/
└── ProviderEndpoints.cs        # POST /providers/test  (registered into the 005 API; stub host until then)
```

## Config shape (the single source of truth)

```jsonc
"provider": {
  "type": "openai_compatible",      // anthropic | openai | gemini | openai_compatible
  "model": "qwen3:8b",
  "base_url": "http://localhost:11434/v1",  // required for openai_compatible; ignored otherwise
  "api_key_ref": "lore/provider"            // handle into Credential Manager; "" for keyless local
}
```

`ProviderSelector` maps `type` → backend with **no fallback chain**. Missing/invalid
config yields a clear "configure a provider" error, not a silent default.

## Secret storage

`ICredentialStore` wraps Windows Credential Manager (DPAPI). Keys are written under
a handle (`api_key_ref`); backends resolve the handle at call time. `config.json`
never holds key material; a logging filter guarantees keys can't be written to
`lore.log`. A one-time helper migrates any inline v1 key into the store, then
strips it from config.

## Test connection

`POST /providers/test` builds the selected backend from a supplied (or current)
provider config, sends a tiny prompt, and returns `{ok, model, latency_ms}` or
`{ok:false, error}` with specific messages for: unauthorized (bad key), unreachable
(bad URL/host), and model-not-found. This endpoint is defined here and surfaced by
005; the app (010) calls it from Settings/onboarding.

## Provider → mem0 bridge

`Mem0ProviderBridge` turns the `provider` block into memoryd's `POST /config`
payload (002): LLM type/model/base_url/key, plus an embedder. When the provider has
no first-party embeddings (e.g. Anthropic) or is keyless-local, the bridge selects
the **local embedder default** (002's `follow_provider`). Result: one provider
choice configures capture analysis *and* memory.

## Deletions (v1 → gone)

`ProxyInferenceBackend`, `GeminiBatchService`, `OnnxGenAiBackend`,
`HardwareDetector`, `ModelDownloader`, and the priority logic inside
`InferenceManager` are **not ported**. Local-model users are served by
`openai_compatible` + Ollama/LM Studio, documented in `docs/providers.md`.

## Decisions

- **Config is the whole truth.** No hosted default, no GPU gating, no fallback
  chain — selection is a pure function of `provider.type`.
- **Keys in the keystore, referenced by handle** — a cleanliness *and* trust upgrade
  over v1's config-file keys.
- **One provider, two consumers.** An optional future override (cheap local model
  for capture, stronger model for memory) is left as a documented extension point,
  not built now.

## Dependencies & order

Upstream: **002** (bridge target). Parallel: **003** (consumes `IInferenceBackend`,
mocked until this merges). Internal order: abstraction + selector → the four
backends → credential store → test endpoint → mem0 bridge → delete v1 cruft +
update privacy/provider docs. See [`tasks.md`](tasks.md).
