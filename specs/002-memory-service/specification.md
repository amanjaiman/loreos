# 002 — Memory Service · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 001.

## Overview

The memory service is Lore's store-and-recall layer. It replaces v1's bespoke
machinery (SQLite + ONNX embeddings + compaction + vector math) with
[mem0](https://github.com/mem0ai/mem0) OSS, accessed through a single C# seam
(`IMemoryService`). mem0 runs inside a bundled Python sidecar, **`memoryd`** (a
small FastAPI service the agent supervises on `127.0.0.1`), so the agent gets
mem0's full feature set — extraction, deduplication, conflict resolution, semantic
search — without the C#/Python boundary leaking into the rest of the codebase.

This spec also establishes the **agent host**: the .NET generic/web host and DI
container that later agent specs (003 capture, 005 local API) plug into, plus the
supervisor that spawns, health-checks, and restarts `memoryd`.

Because mem0 quality on Lore's screen-derived input is the single biggest unknown
in the whole project, this spec front-loads a **time-boxed evaluation spike**
before the production service is built.

## User stories

- **As the capture pipeline (003)**, I can call `IMemoryService.Remember(observation)`
  and trust that mem0 will extract, dedupe against existing memories, and store it —
  without me knowing mem0 exists.
- **As any read surface (005/006/007)**, I can call `Search(query)` /
  `GetRecent()` / `GetAll()` and get relevant memories back.
- **As a user**, my memories live on my disk by default, and I can optionally point
  every device at one self-hosted mem0 server to share memory across machines —
  with no Lore-operated infrastructure.
- **As a maintainer**, mem0's fast-moving API is isolated behind one pinned,
  contract-tested seam, so an upstream change can't ripple through the agent.

## Scope

### In scope

- **`memoryd`** Python service: FastAPI over mem0 OSS with on-disk Qdrant, exposing
  add / search / get_all / get / update / delete / health / config.
- **mem0 configuration translation:** memoryd configures mem0's LLM, embedder, and
  vector store from a provider config passed by the agent (the same provider the
  user picks in 004), with a sensible **local embedder default**.
- **`IMemoryService` + `MemorydClient`** (C#): the only seam to memory in the
  entire codebase; an HTTP client of memoryd with typed models and error handling.
- **memoryd supervision:** spawn as a child process, health-check before use,
  restart on crash, shut down cleanly with the agent.
- **Agent host bootstrap:** the WebApplication host + DI container the agent runs
  in (exposing only `/health` here; 005 adds the full API).
- **Remote mode:** `memory.engine: "remote"` points `MemorydClient` at a
  user-hosted mem0 server instead of the local sidecar.
- **Evaluation spike** (gated, time-boxed): feed ~200 real distilled observations
  exported from v1's SQLite through mem0; record extraction/dedup/search/latency
  findings that justify the production design. Includes a **PyInstaller packaging
  proof** that a single-exe sidecar runs on a clean Windows VM.

### Out of scope

- Provider/credential management and the `/providers/test` flow (004) — this spec
  consumes a provider config; it does not build the provider layer.
- The full local REST API and OpenAPI (005).
- Capture (003), buckets and retention tiers (deferred), document import (009).
- Any cloud sync machinery beyond pointing at a user's own remote mem0.

## Acceptance criteria

1. `IMemoryService` exposes `Remember`, `Search`, `GetRecent`, `GetAll`, `Get`,
   `Update`, `Delete`, each round-tripping through memoryd.
2. The agent spawns `memoryd`, waits for `/health` to go green before first use,
   and restarts it if it dies; killing memoryd mid-run is recovered automatically.
3. `add` followed by `search` for the same topic returns the stored memory; adding
   a contradicting fact updates rather than duplicates (mem0's UPDATE behavior is
   exercised and asserted).
4. With only an Anthropic-style key configured (no embedding key), memoryd still
   produces embeddings via the **local default** — no surprise embedding calls.
5. Switching `memory.engine` from `embedded` to `remote` (pointing at a test mem0
   server) requires no code change — only config.
6. Contract tests pass against the **pinned** mem0 version; a CI job runs them.
7. The spike report exists in this spec folder (`spike-findings.md`) with a
   go/no-go on mem0 and on PyInstaller packaging; if either is no-go, the
   documented fallback (mem0 TypeScript in Electron) is recorded as the decision.

## Non-functional requirements

- **Isolation:** no code outside `MemorydClient` imports or references mem0/HTTP to
  memoryd. (Constitution §3.2.)
- **Pinned & reproducible:** mem0 and Qdrant versions pinned; memoryd installs
  deterministically.
- **Latency:** a `search` round-trip on a warm local store returns in a
  human-acceptable time for interactive use (target < ~1.5 s; recorded in the spike).
- **Localhost only:** memoryd binds `127.0.0.1`, never an external interface.

## Risks

- *mem0 extraction underwhelms on screen-derived text.* Mitigation: the spike runs
  first; the capture-analysis distillation (003) is the quality lever; the
  `IMemoryService` seam means a worst-case swap doesn't touch callers.
- *PyInstaller sidecar is flaky on user machines.* Mitigation: packaging proof is
  part of the spike; fallback decided here, not at launch.
- *mem0 API churn.* Mitigation: pinned version + contract tests in CI.
