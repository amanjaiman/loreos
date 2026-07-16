# 005 — Local API (The Spine) · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 002, 003,
> 004. **Depended on by:** 006 (MCP), 007 (CLI), 009 (import), 010 (app).

## Overview

The local API is the **one behavior layer** the constitution mandates (§3.1): a
documented, versioned HTTP API on `127.0.0.1:7842`, backed by `IMemoryService`,
through which every surface — the app, the MCP server, the CLI, future clients —
reads and writes. It exists so that "compatible with everything" costs us thin
shims, not duplicated logic. This spec defines the endpoint contract, ships an
OpenAPI document, and assembles the endpoints (memory CRUD/search, config,
providers/test, system status, export) onto the host that 002 established.

## User stories

- **As a surface author (MCP/CLI/app/anything)**, I have one documented, stable,
  localhost API to build against, so adding a new way to use Lore is a translation,
  not a re-implementation.
- **As a developer**, I can read `/openapi.json` (or the docs) and understand every
  capability Lore exposes in one place.
- **As a security-minded user**, the API binds only to loopback and exposes nothing
  to the network.
- **As a user**, I can export everything Lore knows about me (JSON or Markdown) —
  data ownership, not lock-in.

## Scope

### In scope

- **Memory endpoints** over `IMemoryService`: list, search, get, add, update,
  delete.
- **Recent/activity endpoints**: recent captures and the local activity log (from
  003's `ActivityStore`).
- **Config endpoints**: read/update `config.json` (provider block, capture,
  blocklist, memory engine) — secrets still flow through 004's credential store,
  never echoed.
- **Providers**: surface 004's `POST /providers/test`.
- **System**: `GET /system/status` (agent + memoryd + provider health, version),
  `GET /system/log` (tail), data reset.
- **Export**: `GET /export/json`, `GET /export/markdown`.
- **OpenAPI**: a generated, versioned spec at `/openapi.json` plus a short
  `docs/api.md`.
- **Versioning + localhost binding** as first-class contract guarantees.

### Out of scope

- MCP tool definitions (006), CLI (007), import pipeline logic (009 — this spec
  reserves the route; 009 implements it), app UI (010).
- Any non-loopback listener or auth (there is no remote surface).
- Buckets/retention endpoints (deferred features).

## Acceptance criteria

1. All in-scope endpoints exist, bind to `127.0.0.1:7842` only, and are reachable
   over loopback; binding to an external interface is impossible by default.
2. Memory list/search/get/add/update/delete round-trip through `IMemoryService` and
   return the documented shapes.
3. `GET /system/status` reports agent + memoryd + provider readiness and the app
   version.
4. `GET /export/json` and `/export/markdown` return the full memory set in valid,
   documented formats.
5. Config read/update works and **never** returns or accepts inline secret material
   (keys go via 004's credential store; responses are scrubbed).
6. `/openapi.json` is generated, accurate, and versioned; `docs/api.md` links it.
7. A contract/integration test exercises each endpoint against a seeded store.

## Non-functional requirements

- **The only behavior layer:** surfaces never bypass this API to talk to mem0 or
  models directly (constitution §3.1).
- **Loopback only** (constitution §3.4); no auth because there is no remote access.
- **Stable + versioned:** breaking changes are versioned; the OpenAPI doc is the
  contract surfaces pin to.
- **No secret echo:** config responses and logs never contain key material.

## Risks

- *Surfaces drift from the contract.* Mitigation: OpenAPI is the single source;
  006/007 build against it; contract tests guard it.
- *Endpoint sprawl.* Mitigation: scope is fixed here; new endpoints require a spec
  amendment, keeping the "thin shim" discipline.
