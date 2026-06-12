# 002 — Memory Service · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Two halves joined by one HTTP seam:

1. **`memoryd`** (Python/FastAPI) wraps mem0 and owns all memory logic.
2. **`MemorydClient : IMemoryService`** (C#) is the agent's only door to it.

The agent supervises memoryd as a child process. Everything mem0-shaped lives in
Python; everything else in the codebase sees only `IMemoryService`.

## memoryd (Python)

```
memoryd/
├── pyproject.toml                 # pins mem0, qdrant, fastapi, uvicorn (+ dev tools)
├── lore_memoryd/
│   ├── app.py                     # FastAPI app + lifespan (init mem0 from config)
│   ├── routes.py                  # /memories CRUD, /search, /health, /config
│   ├── backend.py                 # MemoryBackend Protocol + Mem0Backend adapter (typed seam)
│   ├── mem0_factory.py            # build mem0 Memory() from a provider config
│   ├── reconcile.py               # post-add conflict reconciliation (spike Option C, T003)
│   └── models.py                  # pydantic request/response models (HTTP wire contract)
└── tests/                         # contract tests against pinned mem0
```

**Routes (all `127.0.0.1` only):**

| Method · Path | Purpose |
|---|---|
| `GET /health` | readiness (`{"status":"ok"}`) |
| `POST /config` | (re)initialize mem0 with a provider config |
| `POST /memories` | add: `{text, user_id, metadata}` → mem0 add **+ reconciliation pass** (extract/dedup, then near-neighbor supersede/duplicate resolution — see Decisions) |
| `POST /memories/search` | `{query, user_id, limit, filters}` → ranked results |
| `GET /memories` | get_all for a user (paged) |
| `GET /memories/{id}` · `PATCH /memories/{id}` · `DELETE /memories/{id}` | single-item ops |

**`mem0_factory.py`** translates Lore's provider config (type + model + base_url +
key handle, from 004) into mem0's `config` dict for LLM, embedder, and vector
store. The vector store is on-disk **Qdrant** under the Lore data dir. The embedder
**defaults to a local model** (e.g. an Ollama or small HF embedder via mem0) when
the chosen provider has no first-party embeddings, so a user with only a chat-model
key never incurs surprise embedding bills.

## C# seam

```
agent/Memory/
├── IMemoryService.cs       # Remember, Search, GetRecent, GetAll, Get, Update, Delete
├── MemorydClient.cs        # HttpClient impl; typed models; error mapping; ConfigureAsync (T005)
├── MemorydSupervisor.cs    # BackgroundService: spawn, health-gate, restart, dispose
├── MemoryModels.cs         # MemoryRecord, AddedMemory; MemoryConfig / provider / embedder records
└── MemorydException.cs     # Thrown on unexpected HTTP status; carries operation + status + body
```

`IMemoryService` is intentionally narrow and mem0-agnostic (no mem0 vocabulary
leaks into method names). `MemorydSupervisor` is a hosted `BackgroundService` that
launches the bundled memoryd (dev: `python -m lore_memoryd`; packaged: the
PyInstaller exe), polls `/health` with backoff, exposes a `Ready` gate other
services await, and restarts memoryd on unexpected exit.

## Agent host bootstrap (shared foundation for 003/005)

This spec stands up the agent's host so later specs add to it rather than invent
their own:

- `Program.cs` builds a `WebApplication` (ASP.NET Core) with DI; in this spec it
  registers `LoreConfig`, `IMemoryService`/`MemorydClient`, and `MemorydSupervisor`,
  and maps **only** `/health`.
- 003 registers capture services into the same host; 005 adds the full REST API.
- `--mcp` mode (006) reuses the DI graph without the HTTP listener.

> **Cross-spec decision (recorded per constitution §9):** the agent host and DI
> container are owned by this spec. 003 and 005 extend it; they do not create a
> second host.

## Config

```jsonc
"memory": {
  "engine": "embedded",        // "embedded" (bundled sidecar) | "remote"
  "remote_url": "",            // used when engine = "remote"
  "embedder": { "type": "follow_provider" }  // or an explicit local override
}
```

Provider details (LLM type/model/base_url/key) come from 004's `provider` block and
are forwarded to memoryd's `POST /config`; this spec defines the forwarding, not the
provider UI.

## The spike (do this first)

> **Status: complete — GO** on mem0 and on PyInstaller packaging. See
> [`spike-findings.md`](spike-findings.md). The corpus was synthetic (no v1 `lore.db`
> on hand) and the clean-VM packaging check is deferred to the installer spec; both
> are recorded as caveats. The one design-affecting result (additive `add()`) is
> captured in Decisions above.

A throwaway branch, time-boxed, producing `spike-findings.md` in this folder:

1. Export ~200 distilled observations from a v1 `lore.db` (a small read-only
   script; **not** a shipped migration — answer to open-question 4 is "no migration").
2. Run mem0 + on-disk Qdrant + a local model (Ollama) over them; evaluate
   extraction quality, dedup/update behavior, search relevance, latency.
3. PyInstaller-bundle the prototype; run the single exe on a clean Windows VM.
4. Record go/no-go. If mem0 or packaging is no-go, the recorded decision is the
   fallback (mem0 TS SDK in Electron) and this plan is amended before production
   tasks proceed.

## Dependencies & order

Upstream: **001**. Internal order: spike → memoryd routes → mem0_factory + local
embedder default → C# seam + models → supervisor + host bootstrap → remote mode →
contract tests + CI job. See [`tasks.md`](tasks.md).

## Decisions

- **Sidecar over in-process** (Option A from the project plan): full mem0 parity,
  isolates the fast-moving dependency, gives a reusable local seam.
- **Pinned mem0 + contract tests** are mandatory, not optional — this is the churn
  firewall. Spike pinned **`mem0ai==2.0.5`**.
- **Local embedder default** protects users from accidental embedding spend and
  keeps "bring your own key" honest. Spike validated **Ollama `nomic-embed-text`
  (768-dim)**: 10/10 ground-truth search hits, p50 49 ms.
- **`memoryd`-side conflict reconciliation** (spike T001, ratified Option C). mem0
  2.0.x's default `add()` is **additive + exact-hash dedup only** — it does *not*
  emit LLM UPDATE/DELETE, so contradictory/near-duplicate facts accumulate (~5× store
  bloat observed at 200 obs). To satisfy acceptance criterion 3 on the maintained
  mem0 line, `memoryd` runs an explicit reconciliation pass after `add`: near-neighbor
  search → LLM "supersedes / duplicate / unrelated" judgment → mem0 `update()` /
  `delete()` so the store converges to one current fact. See
  [`spike-findings.md`](spike-findings.md) Finding 1.
- **`history_db_path` under the Lore data dir**, never mem0's global `~/.mem0`
  default — the global default leaks per-`user_id` "Last k Messages" across instances
  and poisons extraction (spike Finding 2).
- **mem0 telemetry force-disabled (`MEM0_TELEMETRY=False`).** mem0 OSS ships anonymous
  PostHog telemetry to `us.i.posthog.com` enabled by default — a direct conflict with
  constitution §1.2 (zero telemetry) / §4.3 (closed egress). `memoryd` sets the
  kill-switch in `lore_memoryd/__init__.py` before mem0 imports; this also stops mem0
  creating a second global Qdrant (`~/.mem0/migrations_qdrant`) that would otherwise
  lock across engine rebuilds. Documented in `docs/privacy.md`, guarded by a test.
  (T003 finding.)
- **Minimum capable extraction model.** Memory quality is strongly LLM-bound: a 2B
  model confabulates; a 7B-class (or hosted frontier) model extracts cleanly
  (spike Finding 2). Documented for the provider/onboarding surfaces.
- **`GetAllAsync` paginates with offset/limit** rather than sending a single
  unbounded request. memoryd's `GET /memories` accepts `limit` + `offset`; mem0 has
  no native offset, so `Mem0Backend` slices `[offset:offset+limit]` (O(N) per page →
  O(N²) overall — acceptable for maintenance tooling, not hot-path reads; hot-path
  callers should use `SearchAsync` or `GetRecentAsync`). `MemorydClient.GetAllAsync`
  loops until a short page signals the end; page size defaults to 500 and is
  constructor-injectable so tests can drive pagination with tiny pages.
- **`ConfigureAsync` is on `MemorydClient` directly, not `IMemoryService`.** It is a
  lifecycle operation (the T005 supervisor calls it once memoryd is healthy), not a
  memory-ops call. Keeping it off the interface prevents callers from re-initializing
  the engine accidentally and keeps the seam narrow.
