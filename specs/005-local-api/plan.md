# 005 — Local API · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Assemble an ASP.NET Core Minimal API onto the host 002 created, grouping endpoints
by concern, each delegating to an injected service (`IMemoryService`, `LoreConfig`,
`ICredentialStore`, capture metrics/activity store). Generate OpenAPI from the
endpoint definitions so the contract can't drift from the code. Bind loopback-only.

## Structure

```
agent/Api/
├── ApiHost.cs                  # maps endpoint groups onto the 002 WebApplication; Kestrel → 127.0.0.1:7842
└── Endpoints/
    ├── ContextEndpoints.cs     # /memories list/search/get/add/update/delete  (IMemoryService)
    ├── RecentEndpoints.cs      # /recent, /activity                            (ActivityStore, 003)
    ├── ConfigEndpoints.cs      # /config read/update (secret-scrubbed)
    ├── ProviderEndpoints.cs    # /providers/test                              (from 004)
    ├── SystemEndpoints.cs      # /system/status, /system/log, /system/data (reset)
    └── ExportEndpoints.cs      # /export/json, /export/markdown
docs/api.md                     # human guide + link to /openapi.json
```

## Endpoint contract (summary)

| Method · Path | Backed by | Notes |
|---|---|---|
| `GET /memories` | IMemoryService.GetAll | paged |
| `POST /memories/search` | IMemoryService.Search | `{query, limit, filters}` |
| `GET /memories/{id}` · `POST /memories` · `PATCH /memories/{id}` · `DELETE /memories/{id}` | IMemoryService | CRUD |
| `GET /recent` · `GET /activity` | ActivityStore (003) | local telemetry |
| `GET /config` · `PATCH /config` | LoreConfig | **secret-scrubbed**; keys via 004 |
| `POST /providers/test` | 004 | model + latency or actionable error |
| `GET /system/status` | supervisor + selector | agent/memoryd/provider health + version |
| `GET /system/log` · `DELETE /system/data` | log tail · reset | |
| `GET /export/json` · `GET /export/markdown` | IMemoryService | full export |
| `GET /openapi.json` | generated | the contract surfaces pin to |

## Binding & versioning

- Kestrel is configured to listen **only** on `127.0.0.1:7842`. No `0.0.0.0`, no
  external interface, ever. A startup assertion fails fast if a non-loopback bind is
  configured.
- The API carries a version (header + OpenAPI `info.version`). Breaking changes bump
  it; 006/007 pin to it.
- No auth: there is no remote surface to authenticate. This is a deliberate
  consequence of loopback-only, documented in `docs/api.md`.

## Secret scrubbing

`ConfigEndpoints` reads/writes `config.json` but runs every response through a
scrubber that strips anything resolving to a credential handle's value. `PATCH`
accepts a key only as "store this and replace with a handle" — it is handed to
004's `ICredentialStore` and never persisted inline.

## OpenAPI

Generated from the Minimal API metadata (built-in OpenAPI document). `docs/api.md`
is a short narrative that links `/openapi.json` and shows a couple of curl
examples. The generated doc is the authority; the markdown is the welcome mat.

## Decisions

- **One API host, assembled here, on 002's WebApplication** — not a second host
  (constitution §3.3 + the 002 cross-spec decision).
- **Loopback-only ⇒ no auth** — simpler and safer than an auth layer guarding a
  surface that has no remote access.
- **Generated OpenAPI is the contract** — surfaces build against it; drift is caught
  by contract tests, not code review alone.
- **Import route reserved, not implemented** — 009 fills `POST /import`; this spec
  leaves the documented placeholder so the contract is stable.

## Dependencies & order

Upstream: **002** (host + memory), **003** (activity store), **004** (config,
credential store, providers/test). Internal order: API host + loopback binding →
memory endpoints → recent/activity → config (scrubbed) → system + export → wire
004's providers/test → OpenAPI + docs + contract tests. See [`tasks.md`](tasks.md).
