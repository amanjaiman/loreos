# 004 — Provider Layer · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — `IInferenceBackend` abstraction + config-driven selector  `[deps: spec 002]`
Port `IInferenceBackend` + `InferenceResult` (cleaned). Implement `ProviderConfig`
(typed view over the `provider` block) and `ProviderSelector` mapping `type` → one
backend with **no priority/fallback magic**; invalid config → clear error.
**Done when:** selection is a pure, unit-tested function of config; bad config
yields an actionable "configure a provider" error.

### T002 — Anthropic + OpenAI backends  `[deps: T001]`
Implement `AnthropicBackend` (messages API) and `OpenAiBackend` (chat completions),
both honoring `maxTokens` and returning `InferenceResult`.
**Done when:** both run a prompt against mocked HTTP in tests; live-check steps
documented.

### T003 — Gemini + OpenAI-compatible backends  `[deps: T001]`
Port/clean `GeminiBackend` from v1; implement `OpenAiCompatibleBackend` (base-URL
chat completions, optional key).
**Done when:** `openai_compatible` works against a local Ollama endpoint by base
URL (acceptance criterion 2); Gemini covered by mocked-HTTP tests.

### T004 — Credential store  `[deps: spec 002]`
Implement `ICredentialStore` over Windows Credential Manager (DPAPI) and a logging
filter that prevents key material from reaching `lore.log`. Add the one-time
inline-key → keystore migration.
**Done when:** keys round-trip via handle; a test asserts no key in `config.json`
and no key in logs (acceptance criterion 3).

### T005 — `POST /providers/test`  `[deps: T002, T003, T004]`
Implement the test-connection endpoint: build the selected backend, send a tiny
prompt, return `{ok, model, latency_ms}` or specific errors (unauthorized /
unreachable / model-not-found). (Registered into the 005 host when it exists; until
then exercised via a thin test host.)
**Done when:** success and each failure mode return the documented shape; covered by
tests.

### T006 — Provider → mem0 bridge  `[deps: T002, T003]`
Implement `Mem0ProviderBridge`: provider block → memoryd `POST /config` payload
(LLM + embedder), selecting the local-embedder default when the provider lacks
embeddings. Wire it so configuring a provider reconfigures memoryd.
**Done when:** one provider choice drives both capture analysis and memory; the
no-embeddings case uses the local default (acceptance criterion 5).

### T007 — Delete v1 cruft + update docs  `[deps: T001–T006]`
Confirm `ProxyInferenceBackend`, `GeminiBatchService`, `OnnxGenAiBackend`,
`HardwareDetector`, `ModelDownloader`, and priority logic are absent. Write
`docs/providers.md` (the four types + local setup with Ollama) and update
`docs/privacy.md`'s egress list.
**Done when:** none of the deleted components exist; provider + privacy docs are
accurate (acceptance criterion 6).

---

## Definition of done for spec 004

All four provider types work from config alone; secrets live in the keystore and
never leak; `/providers/test` gives clear results; one provider config drives both
analysis and memory; v1's hosted/in-process/priority machinery is gone. Capture
(003) can integrate the real backend, and the app (010) can build provider settings.
