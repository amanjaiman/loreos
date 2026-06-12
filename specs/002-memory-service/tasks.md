# 002 — Memory Service · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisite tasks (T0xx within this spec; spec
> IDs for cross-spec).

---

### T001 — Evaluation + packaging spike  `[deps: spec 001]`

> **Status: complete — GO** on mem0 and on PyInstaller packaging. See
> [`spike-findings.md`](spike-findings.md). Two planned steps were not executed as
> written: (a) no v1 `lore.db` was available, so a synthetic ~200-observation corpus
> was authored instead (representative-not-real, recorded as a caveat); (b) the
> clean-VM packaging check ran on the dev machine only — a pristine-VM run is deferred
> to spec 011 T001. Both caveats are in `spike-findings.md`.

On a throwaway branch: export ~200 distilled observations from a v1 `lore.db`
(read-only script), run mem0 + on-disk Qdrant + a local model over them, and
PyInstaller-bundle the prototype on a clean Windows VM. Write `spike-findings.md`
(extraction/dedup/search/latency results, packaging result, go/no-go + fallback).
**Done when:** `spike-findings.md` is committed with an explicit go/no-go; if
no-go, `plan.md` is amended to the fallback before any further task starts.
*(Spike code is not merged into the product; the report is the deliverable.)*

### T002 — memoryd skeleton → real routes  `[deps: T001]`

> **Status: complete.** Routes, pydantic models, `mem0_factory.py`, and the typed
> `backend.py` seam all landed here. The `follow_provider` embedder refinement and
> `reconcile.py` are the remaining T003 deliverables.

Grow the foundation `memoryd` from `/health`-only into the full route set
(`/config`, `/memories` add/search/get_all/get/update/delete) with pydantic models.
Pin mem0 + Qdrant in `pyproject.toml`. Wire mem0 in app lifespan.
**Done when:** routes work against a local mem0 with a test config; `ruff`, `mypy`,
`pytest` green.

### T003 — follow_provider embedder + reconciliation  `[deps: T002]`

> **Status: complete.** `follow_provider` OpenAI first-party path, `reconcile.py`
> (spike Option C), `Mem0Backend.close()` + `/config` disposal, and the
> `MEM0_TELEMETRY=False` kill-switch all landed here.

Complete the `follow_provider` embedder policy in `mem0_factory.py`: prefer the
provider's own first-party embeddings (e.g. OpenAI) before falling back to the local
Ollama default (`nomic-embed-text`, 768-dim). Implement `reconcile.py` (spike Option
C): after `add`, near-neighbor search → LLM supersede/duplicate/unrelated judgment →
mem0 `update()`/`delete()`, since mem0 2.0.x's `add()` is additive-only (spike
Finding 1).
**Done when:** a provider config with no embedding capability still yields working
search via the local embedder; a contradicting fact converges to one current memory
via reconciliation; both covered by tests.

### T004 — `IMemoryService` + `MemorydClient` + models  `[deps: T002]`

> **Status: complete.** `IMemoryService` (7 methods), `MemorydClient` (sealed typed
> HTTP client, snake_case JSON, `GetAllAsync` offset/limit pagination, `ConfigureAsync`
> kept off the interface — lifecycle, not memory-ops), `MemoryModels`, and
> `MemorydException` all landed here. memoryd's `GET /memories` gained `limit` +
> `offset` query params to support client-side pagination. 14 C# unit tests (no
> network; `StubHttpMessageHandler`); `.editorconfig` gains a `*.tests` section
> relaxing CA1707/CA2007/CA1062 for xUnit conventions.

Define `IMemoryService` (Remember/Search/GetRecent/GetAll/Get/Update/Delete) and
implement `MemorydClient` as a typed HTTP client of memoryd, with error mapping. No
mem0 vocabulary in the interface.
**Done when:** unit tests cover client request/response shaping against a mocked
memoryd; the interface is the only memory seam in `agent/`.

### T005 — Agent host bootstrap + memoryd supervisor  `[deps: T004]`
Build the `WebApplication` host + DI in `Program.cs` (registering config,
`IMemoryService`, supervisor; mapping only `/health`). Implement `MemorydSupervisor`
(spawn, health-gate with backoff, restart-on-crash, clean shutdown). The health-gate
backoff must allow for PyInstaller `--onefile` cold-start latency (the exe unpacks to
a temp dir on first launch; spike Finding 4 measured a few seconds) — size backoff
budget accordingly, or switch to `--onedir` in the installer spec to eliminate
extraction overhead.
**Done when:** the agent launches memoryd, gates on `/health`, and auto-recovers
when memoryd is killed mid-run (integration test or documented manual check).

### T006 — Remote engine mode  `[deps: T004]`
Support `memory.engine: "remote"`: `MemorydClient` targets a user-hosted mem0
server (no sidecar spawned). Document the self-hosted multi-device setup in
`docs/multi-device.md` (stub; expanded in 011).
**Done when:** flipping config to `remote` against a test server works with zero
code change; covered by a test using a stand-in server.

### T007 — Contract tests + CI job  `[deps: T002, T004]`
Add contract tests asserting mem0's add/search/update/delete behaviors against the
pinned version, and a CI job that runs them. Assert acceptance criterion 3 explicitly:
adding a contradicting fact leaves **one** memory reflecting the new truth, via
`memoryd`'s reconciliation pass (spike Option C) — not mem0's native `add()`, which
is additive-only on the pinned 2.0.x.
**Done when:** the contract suite is green in CI against the pinned mem0; bumping
mem0 is a deliberate, test-guarded change.

---

## Definition of done for spec 002

All acceptance criteria pass; memory is reachable only via `IMemoryService`; the
agent supervises memoryd reliably; embedded and remote engines both work from
config alone; contract tests guard the pinned mem0 in CI; the spike report justifies
the design. Specs 003, 004, 005, and 009 can now build on the memory seam.
