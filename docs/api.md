# Local API — the one behavior layer

> Lore exposes exactly one behavior surface (constitution §3.1): a documented,
> versioned HTTP API on `127.0.0.1:7842`, backed by the memory service. The app,
> the MCP server (spec [006](../specs/006-mcp-server)), the CLI (spec
> [007](../specs/007-cli)), and any future client are **thin shims over this API** —
> they translate, they don't re-implement. This document is the welcome mat; the
> generated [`/openapi.json`](#openapi) is the authority.

## At a glance

- **Base URL:** `http://127.0.0.1:7842`
- **Bind:** loopback only. The host refuses to start if configured to bind any
  non-loopback interface (constitution §3.4), so nothing is ever exposed to the
  network.
- **Auth:** none — there is no remote surface to authenticate. This is a
  deliberate consequence of loopback-only, not an omission.
- **Version:** `1.0` (carried in OpenAPI `info.version`). Breaking changes bump it;
  surfaces pin to it.
- **Format:** JSON request/response (snake_case fields), except `GET
  /export/markdown`, which returns `text/markdown`.

## Endpoints

| Method · Path | Does | Notes |
|---|---|---|
| `GET /health` | Liveness | `{ "status": "ok" }` |
| `GET /memories` | List memories | Paged: `?limit=&offset=` → `{ items, total, limit, offset }` |
| `POST /memories/search` | Semantic search | `{ query, limit?, user_id?, filters? }` → `{ results }` (scored). `filters` is reserved. |
| `GET /memories/{id}` | One memory | `404` if absent |
| `POST /memories` | Remember an observation | `{ text, user_id?, metadata? }` → `201 { results }` (the stored memories) |
| `PATCH /memories/{id}` | User-authority edit | `{ text?, pinned?, kind? }` → updated memory (stamps `user_edit`; text edits set confidence 1.0), `404` if absent |
| `DELETE /memories/{id}` | Forget one | `204`, or `404` if absent |
| `GET /recent` | Recent raw captures | Local telemetry (`?limit=`), newest first |
| `GET /activity` | Activity log | What Lore did per window (`?limit=`), newest first |
| `GET /config` | Read config | Secret-scrubbed — no key material is ever returned |
| `PATCH /config` | Update config | Deep-merges; an inline `provider.api_key` is moved to the OS keystore and replaced by a handle |
| `POST /providers/test` | Test a model config | `{ type?, model?, base_url?, api_key_ref? }` → model + latency, or an actionable error |
| `GET /system/status` | Component readiness | `agent` / `memoryd` / `provider` state + app & API version |
| `GET /system/log` | Tail the log | `?lines=` → `{ lines }`; already redacted |
| `DELETE /system/data` | Reset memory | Forgets every memory (the activity log is left intact) |
| `GET /export/json` | Export everything (JSON) | `{ exported_at, count, memories }` |
| `GET /export/markdown` | Export everything (Markdown) | `text/markdown` |
| `POST /recall` | The every-turn memory check | `{ query, k?, kinds? }` → `{ results }` of typed facts above the relevance floor (often, correctly, empty); zero generative calls |
| `POST /memories/{id}/confirm` | Still-true confirmation | Pushes a `state` fact's horizon out; re-stamps others |
| `POST /staging/{id}/promote` | Keep a staged candidate | User authority — ignores the daily budget; `409` if not staged |
| `POST /staging/{id}/dismiss` | Dismiss a staged candidate | Archives it; `409` if not staged |
| `GET /episodes` · `GET /episodes/{id}` | What the distiller saw | Provenance for memories (`?limit=`) |
| `GET /decisions` | The decision trail | Why Lore did/didn't remember something (`?limit=`) |
| `GET /system/economy` | Today's capture economy | Decision counts since UTC midnight vs the promotion budget |

### Secrets

`GET /config` never returns key material: inline secret fields are stripped at any
depth, and only the `api_key_ref` **handle** is returned. `PATCH /config` accepts a
key only as "store this and replace with a handle" — it is handed to the OS
credential store and never persisted inline (constitution §4.2). See
[`providers.md`](providers.md).

## Examples

```sh
# Is it up, and what's ready?
curl http://127.0.0.1:7842/system/status

# Search memory
curl -X POST http://127.0.0.1:7842/memories/search \
  -H 'content-type: application/json' \
  -d '{"query":"timezone","limit":5}'

# Remember something
curl -X POST http://127.0.0.1:7842/memories \
  -H 'content-type: application/json' \
  -d '{"text":"I prefer concise answers"}'

# Change just the model (deep-merged into config.json)
curl -X PATCH http://127.0.0.1:7842/config \
  -H 'content-type: application/json' \
  -d '{"provider":{"model":"gpt-4o-mini"}}'

# Take everything Lore knows about you
curl http://127.0.0.1:7842/export/json   > lore-export.json
curl http://127.0.0.1:7842/export/markdown > lore-export.md
```

## OpenAPI

The machine-readable contract is generated from the endpoint definitions, so it
can't drift from the code:

```
GET http://127.0.0.1:7842/openapi.json
```

`info.version` is the API version surfaces pin to. When it changes incompatibly,
the version bumps and clients update deliberately. The contract suite
(`agent.tests`) exercises every endpoint and asserts this document stays accurate.
