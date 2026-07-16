# 013 — First-Run Capture Reliability · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. `[deps: …]` lists prerequisites.
>
> **Validation for this spec:** local checks + CI, **not** the `no-mistakes` gate
> (owner's environment — see [`plan.md`](plan.md#validation-note-this-spec-only)).
> "Done when" means: for agent tasks, `dotnet build -warnaserror`, `dotnet format
> --verify-no-changes`, and `dotnet test` are green; for memoryd tasks,
> `pytest memoryd/tests` is green; and CI is green after merging to `main` with plain
> git.

---

### T001 — Capture P/Invoke entry point  `[deps: none]` — ✅ done (this session)
Bind `Win32ForegroundWindowSource.GetWindowTextLength` to its real export:
`[LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW", …)]`. Under
`[LibraryImport]` the exact name is bound and `user32` exports only the `W`/`A`
variants, so the bare name threw `EntryPointNotFoundException` on every poll. Mirrors the
sibling `GetWindowText`/`GetWindowTextW` import.
**Done when:** a real foreground-window poll reads the title with no exception and the
capture loop produces observations on a packaged build (spec criterion 6). *Implemented
and verified against the installed app in the session that found it.*

### T002 — Live provider reconfigure on `PATCH /config`  `[deps: none]` — ✅ done (this session)
Close the startup-only wiring gap so onboarding takes effect without a restart: a
`ReloadableInferenceBackend` wrapper the capture analyzer holds, an `IProviderReloader`
that re-reads the provider block, rebuilds the inference backend, and re-runs
`Mem0Configurator.ConfigureAsync` in place, and a `PATCH /config` trigger that fires only
when the `provider` block changed. `Mem0Configurator` registered as a resolvable
singleton so the reloader can re-invoke it.
**Done when:** saving a provider against a running agent enables analysis and configures
memoryd with no restart (spec criterion 7). Covered by
`ConfigEndpointsTests.Patch_touching_the_provider_triggers_a_reload`. *Done; 437/437
agent tests green.*

### T003 — Known-provider embeddings in the bridge  `[deps: none]`
In `Mem0ProviderBridge.ToMemoryConfig`, emit an **explicit** embedder for the providers
whose embeddings Lore knows, instead of leaving it unset (which falls through to the
removed Ollama default):
- `gemini` → `openai`-type embedder at the **same** Gemini OpenAI-compat base URL as the
  LLM, model + dims confirmed against the live endpoint (candidate `text-embedding-004`,
  768) and pinned.
- `openai` → confirm/keep `text-embedding-3-small` (1536).
Add the dimensions to `mem0_factory._EMBED_DIMS`. Other kinds are handled by T004/T005.
**Done when:** with **no Ollama running**, both `openai` and `gemini` configs initialize
the memoryd engine and store a memory retrievable via `GET /memories`; one key drives LLM
**and** embedder (spec criteria 1, 2). New `mem0_factory` + bridge unit tests cover each
embedder block and its dimension.

### T004 — Explicit user-configured embedder, end-to-end  `[deps: none]`
Make the `embedder` escape hatch real across the stack — this is what makes "else require
config" possible:
- Lore config gains an optional `embedder` block; bind it in the agent (an
  `EmbedderOptions` beside `ProviderOptions`).
- Carry it through the agent `MemoryConfig`/`MemorydClient` payload into memoryd's
  existing `EmbedderConfig` (type/model/base_url/dims), which is currently never populated
  from outside.
- `Mem0ProviderBridge` precedence: **explicit embedder > known-provider default (T003) >
  self-host endpoint (T005) > none**. Reuse the provider key when the embedder shares the
  host; never require re-pasting it; never log it.
**Done when:** a provider with no first-party embeddings (e.g. Anthropic) **plus** an
explicit `embedder` pointing at a reachable endpoint configures and stores via that
embedder (spec criterion 4); the key is never logged. Unit tests cover precedence and key
reuse.

### T005 — Self-host targeting + narrowed fallback + actionable errors (memoryd)  `[deps: T003]`
Remove the implicit `localhost` Ollama assumption and make the remaining cases honest:
- `openai_compatible` embedder targets the provider's **own `base_url`** (reusing its key
  when keyed); the Ollama-shaped self-host default embed model resolves at *that* URL, not
  `localhost`.
- `follow_provider` no longer invents a local Ollama for providers without first-party
  embeddings. When no embedder can be resolved (none configured and none first-party, or a
  configured/self-host endpoint is unreachable), `POST /config` returns an **actionable**
  error naming the provider, the endpoint/model tried, and the exact keys to set
  (`embedder.type|model|base_url|dims`) — replacing "failed to initialize memory engine."
  Never include key material.
**Done when:** a self-hosted endpoint serving embeddings at its own URL configures and
stores with no `localhost` assumption (criterion 3); a no-embeddings provider with no
embedder returns the actionable, key-free message (criterion 5); the local-first Ollama
path (embeddings present) still works (constitution / 004 criterion 2). Unit tests assert
the error shape, URL targeting, and key-free guarantee.

### T006 — Packaged first-run smoke test  `[deps: T003, T004, T005, spec 011]`
Add an automatable smoke test exercising the **cloud-only first run** against a **real**
memoryd with **no Ollama present**: bring up the native payload (agent + frozen memoryd),
configure a cloud provider via `PATCH /config`, drive/inject one capture, and assert a
memory through `GET /memories`. Also assert the **actionable-error** path: a no-embeddings
provider with no embedder returns the clear message, not a crash. Two legs — a
**deterministic** leg (stub embedder, always runs, proves wiring) and a **live** leg gated
on a CI-secret provider key (skip-with-warning when absent). Wire into CI as a release gate
and document local invocation.
**Done when:** the smoke test passes on a clean (no-Ollama) environment and **fails** if
criteria 1/3/4/5/6/7 regress; it is wired into CI and documented (spec criterion 8).

### T007 — Docs: embeddings matrix, embedder config, privacy  `[deps: T003, T004, T005]`
- `docs/providers.md`: the per-provider embeddings matrix (who is zero-config, who needs
  an explicit embedder), the Gemini/OpenAI embedding models + dims used, and how to
  configure an explicit `embedder` (OpenAI-compatible or Ollama endpoint) for Anthropic /
  OpenRouter / Groq / air-gapped self-host.
- `docs/privacy.md`: confirm/clarify that embeddings use the configured provider's or the
  user's own endpoint (no new destination); correct any wording implying cloud providers
  make no embedding calls. The egress table gains **no** new row.
**Done when:** docs match implemented behavior; the privacy egress table diff is empty
(constitution §4.3).

---

## Definition of done (spec)

All eight acceptance criteria in
[`specification.md`](specification.md#acceptance-criteria) hold. In particular: known
providers (OpenAI, Gemini) and self-hosted-with-embeddings are zero-config on a clean,
Ollama-free machine (1–3); an explicit embedder works for providers without first-party
embeddings (4); the no-embedder case is an actionable, key-free error rather than a silent
localhost assumption (5); the two agent fixes hold (6–7); and the packaged first-run smoke
test gates releases (8). The memoryd change ships in the next packaged release
(human-gated); the live smoke leg runs when a provider key is available, skipped-with-
warning otherwise.
