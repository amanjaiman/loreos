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
│   ├── mem0_factory.py            # build mem0 Memory() from a provider config
│   └── models.py                  # pydantic request/response models
└── tests/                         # contract tests against pinned mem0
```

**Routes (all `127.0.0.1` only):**

| Method · Path | Purpose |
|---|---|
| `GET /health` | readiness (`{"status":"ok"}`) |
| `POST /config` | (re)initialize mem0 with a provider config |
| `POST /memories` | add: `{text, user_id, metadata}` → mem0 add (extract/dedup/update) |
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
├── MemorydClient.cs        # HttpClient impl; typed models; error mapping
├── MemorydSupervisor.cs    # BackgroundService: spawn, health-gate, restart, dispose
└── MemoryModels.cs         # Memory record + result types
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
  firewall.
- **Local embedder default** protects users from accidental embedding spend and
  keeps "bring your own key" honest.
