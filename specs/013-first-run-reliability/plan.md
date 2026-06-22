# 013 — First-Run Capture Reliability · Plan

> SDD artifact: **the how.** Decisions and approach behind
> [`specification.md`](specification.md). Bound by
> [`constitution.md`](../../constitution.md).

## Approach in one paragraph

Three first-run defects share one root cause: **no test ever ran the packaged app's
cloud-only first run end-to-end.** Two are agent-only and already fixed (the
`GetWindowTextLength` entry point; live reconfigure on `PATCH /config`). The third is a
flawed embedder policy: memoryd uses the provider's embeddings only for literal OpenAI
and otherwise silently assumes a local Ollama — breaking Gemini, Anthropic,
OpenRouter/Groq, and any self-hosted endpoint without an embed model. The fix is a
**coherent embedder policy across all providers**: use first-party embeddings where Lore
knows them (OpenAI, Gemini), use the user's own endpoint for self-hosted, accept an
**explicit user-configured embedder** for everything else, and **fail with an actionable
message** instead of assuming localhost. The structural fix is a **packaged-install smoke
test** for the cloud-only first run, so this class of gap fails CI, not the user.

## Key decisions

- **Embedder strategy: provider/self-host embeddings, else explicit config (owner's
  choice).** Lore ships **no** embedding model. The policy, in priority order, for each
  `/config`:
  1. **Explicit `embedder` block present** → use it verbatim (type/model/base_url/dims,
     reusing the provider key where the embedder shares the host). The universal escape
     hatch.
  2. **Provider has embeddings Lore knows** → emit them automatically:
     `openai` → `text-embedding-3-small` (1536); `gemini` → `text-embedding-004` (768)
     via the **same** Google OpenAI-compat base URL the LLM uses.
  3. **`openai_compatible` self-hosted** → target the provider's **own `base_url`** (not
     `localhost`), reusing its key when keyed; for the Ollama-shaped case default the
     embed model (e.g. `nomic-embed-text`) at *that* URL.
  4. **Otherwise** (Anthropic; chat-only compatibles like OpenRouter/Groq; a self-host
     with no embed model) → **no default.** memoryd returns an actionable error; the user
     supplies an explicit embedder (step 1).

- **Fix the known-provider cases in the C# bridge, keep memoryd conservative.**
  `Mem0ProviderBridge` knows the real `ProviderKind` before it collapses Gemini into
  memoryd's `openai_compatible`, so it is the right place to attach the explicit Gemini
  (and OpenAI) embedder block. memoryd's `follow_provider` is **narrowed**: it stops
  inventing a `localhost` Ollama. Where the bridge hasn't supplied an embedder and the
  provider has no first-party embeddings, memoryd raises the actionable error rather than
  guessing — `openai_compatible` is a catch-all (Groq has none, vLLM may not), so guessing
  is wrong by construction.

- **Explicit embedder must be plumbed end-to-end — this is the load-bearing new code.**
  Today the bridge *always* omits the embedder. To make "else require config" real:
  - Lore config gains an optional `embedder` block (and/or `provider.embedder`); bind it
    into the agent (an `EmbedderOptions` alongside `ProviderOptions`).
  - The agent `MemoryConfig`/`MemorydClient` payload carries the embedder so it reaches
    memoryd's existing `EmbedderConfig` (type/model/base_url/dims) — which already exists
    but is never populated from outside today.
  - `Mem0ProviderBridge` precedence: explicit embedder > known-provider default >
    self-host endpoint > (none → memoryd errors).
  - Key reuse: when the embedder shares the provider's host, reuse the resolved key;
    never require the user to paste it twice, never log it.

- **Pin model ids and dimensions; require `dims` on explicit embedders.** Extend
  `mem0_factory._EMBED_DIMS` with the known ids (`text-embedding-3-small` 1536,
  `text-embedding-004` 768, plus the existing Ollama models). Confirm the Gemini embedding
  model id + dimension against the live endpoint during implementation and pin both. An
  explicit embedder must carry `dims` (or resolve from the known map) so Qdrant is sized
  correctly; a genuine mismatch fails clearly rather than corrupting the store.

- **Actionable errors name the fix, not the stack.** When no embedder resolves, memoryd
  returns, e.g.: *"Provider 'anthropic' has no embeddings. Configure an embedder: set
  `embedder.type` (openai|ollama), `embedder.model`, `embedder.base_url`, and
  `embedder.dims`."* It names the provider, the endpoint/model tried (for the unreachable
  case), and the exact keys — and never includes key material (constitution §4).

- **The two agent fixes are already implemented — record, don't re-do.** Defect 1
  (`EntryPoint = "GetWindowTextLengthW"`) and defect 2 (a `ReloadableInferenceBackend`
  seam + `IProviderReloader` invoked by `PATCH /config` when the `provider` block
  changes, rebuilding the inference backend and re-running `Mem0Configurator` in place)
  landed with unit coverage. T001/T002 document them; their "Done when" is satisfied.

- **Smoke test: real memoryd, no Ollama, cloud key.** The missing gate runs the
  **packaged** native payload (agent + frozen memoryd) on an environment with **no
  Ollama**, configures a cloud provider via `PATCH /config`, drives one capture, and
  asserts a memory via `GET /memories`; it also asserts a no-embeddings provider returns
  the actionable error (not a crash). Two legs: a **deterministic** leg (stub embedder,
  always runs, proves wiring) and a **live** leg gated on a CI-secret key
  (skip-with-warning when absent). Running with no Ollama present is precisely the
  condition the unit suite never reproduced.

- **No new egress.** Provider embeddings reuse the user's already-authorized host;
  self-hosted/explicit embedders go where the user pointed. `docs/privacy.md` is reviewed
  and corrected if it implies cloud providers make no embedding calls — not a new egress
  row (constitution §1, §4.3).

## Validation note (this spec only)

Consistent with 011/012 and the owner's environment, tasks are validated with **local
checks + CI**, **not** the `no-mistakes` gate (its console popup interrupts the
desktop). Agent: `dotnet build -warnaserror`, `dotnet format --verify-no-changes`,
`dotnet test`. memoryd: `pytest memoryd/tests` plus new `mem0_factory`/bridge cases.
Merge verified branches to `main` with plain git. Shipping the rebuilt frozen memoryd and
the live smoke key are human-gated.

## Dependencies & order

T001/T002 (already landed) are prerequisites of a *working* first run and part of this
spec's definition of done. The embedder work is: **T003** (known-provider embeddings in
the bridge — OpenAI confirm + Gemini) and **T004** (explicit-embedder plumbing
end-to-end) are the core; **T005** (self-host targeting + narrow `follow_provider` +
actionable errors in memoryd) makes the remaining cases honest. **T006** (smoke test)
depends on T003–T005 and a packaged build (011). **T007** (docs) lands once behavior is
final. Order: T001/T002 (done) → T003 + T004 → T005 → T006 → T007.
