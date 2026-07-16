# 005 — Local API · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — API host + loopback binding  `[deps: spec 002, 003, 004]`
Add `ApiHost` mapping endpoint groups onto the 002 `WebApplication`; configure
Kestrel to listen **only** on `127.0.0.1:7842` with a fail-fast assertion against
non-loopback binds.
**Done when:** the agent serves an empty-but-routable API on loopback; a test
asserts it is unreachable on any external interface (acceptance criterion 1).

### T002 — Memory endpoints  `[deps: T001]`
Implement `/memories` list/search/get/add/update/delete over `IMemoryService` with
documented request/response models.
**Done when:** each verb round-trips through a seeded store and returns the
documented shape (acceptance criterion 2).

### T003 — Recent & activity endpoints  `[deps: T001]`
Implement `/recent` and `/activity` over 003's `ActivityStore` (local telemetry).
**Done when:** both return recent captures / activity-log entries; tests confirm no
mem0 data is conflated with activity data.

### T004 — Config endpoints (secret-scrubbed)  `[deps: T001]`
Implement `GET/PATCH /config` over `LoreConfig`. Route key material to 004's
`ICredentialStore`; scrub all responses so no secret is ever returned.
**Done when:** config round-trips; a test asserts no secret material in any response
or log (acceptance criterion 5).

### T005 — System + export endpoints  `[deps: T002]`
Implement `GET /system/status` (agent + memoryd + provider health + version),
`GET /system/log`, `DELETE /system/data`, and `GET /export/{json,markdown}`.
**Done when:** status reflects real component readiness; exports return valid,
documented JSON/Markdown of the full memory set (acceptance criteria 3, 4).

### T006 — Wire providers/test + reserve import route  `[deps: T001]`
Surface 004's `POST /providers/test` through the API. Add a documented `POST
/import` placeholder returning `501`/"not implemented" for 009 to fill.
**Done when:** `/providers/test` works through the API; `/import` exists in the
contract as a reserved placeholder.

### T007 — OpenAPI + docs + contract tests  `[deps: T002–T006]`
Enable generated OpenAPI at `/openapi.json`; write `docs/api.md` (narrative + curl
examples + link). Add contract/integration tests exercising every endpoint against
a seeded store.
**Done when:** `/openapi.json` is accurate and versioned; `docs/api.md` matches it;
the contract suite is green (acceptance criteria 6, 7).

---

## Definition of done for spec 005

Every in-scope endpoint works over loopback only, backed by `IMemoryService` and
friends; secrets never echo; the OpenAPI contract is generated, accurate, and
versioned; contract tests guard it. The MCP server (006), CLI (007), and app (010)
can now be built as thin shims over this one API.
