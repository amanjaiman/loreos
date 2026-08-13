# The `lore` CLI

`lore` makes Lore usable from a terminal, a script, CI, or a coding agent that
prefers shelling out to a tool protocol. It is a **thin client of the local API**
(spec [005](../../specs/005-local-api)): human-readable by default, `--json` with a
stable schema and meaningful exit codes for machines. It is also Lore's admin
surface (status, config, export) and the home of the installers that wire Lore into
other tools.

> Like every Lore surface, the CLI only talks to `http://127.0.0.1:7842` — your
> machine, your memory (constitution §3.4). Start Lore (the app or the agent) before
> using the read/write commands.

## Global options

| Option | Effect |
|---|---|
| `--json` | Emit a machine-readable JSON envelope instead of human text (see [`--json` contract](#the---json-contract)). |
| `--api-url <url>` | Override the local API base URL. Default `http://127.0.0.1:7842`. |
| `-h`, `--help` | Show help for the command. |

## Commands

| Command | Does | API call |
|---|---|---|
| `lore status` | Component health (agent · memoryd · provider) + version | `GET /system/status` |
| `lore recall "<message>" [--k N]` | Ambient, selective recall for an agent turn | `POST /recall` |
| `lore search "<query>" [--limit N]` | Semantic search over your memory | `POST /memories/search` |
| `lore recent [--limit N]` | Your most recent memories | `GET /memories?limit=N` |
| `lore list [--limit N] [--offset M]` | Browse all memories, page by page | `GET /memories?limit=&offset=` |
| `lore get <id>` | Fetch one memory by id | `GET /memories/{id}` |
| `lore add "<text>" [--category C]` | Remember a new fact (category defaults to `general`) | `POST /memories` |
| `lore forget "<topic>"` | Forget the memory best matching a topic | `POST /memories/search` + `DELETE /memories/{id}` |
| `lore config get [key]` | Read a config value (dot-path) or the whole config | `GET /config` |
| `lore config set <key> <value>` | Change a config value; secrets go to the OS keystore | `PATCH /config` |
| `lore export [--format json\|markdown]` | Export everything Lore knows about you | `GET /export/{json,markdown}` |
| `lore connect [--all]` | Connect agent tools with the portable skill and required MCP shims | *(writes user config)* |

`recent` and `list` both read `GET /memories`; `recent` is the convenient "last N"
view (small default), `list` adds paging. The store returns memories in its own
order, so "recent" is best-effort until a recency-sorted endpoint exists.

## The `--json` contract

Every command accepts `--json`. The output is a single **versioned envelope** so a
script can pin the shape and branch reliably:

```json
{
  "schema_version": "1.0",
  "ok": true,
  "data": { "...": "command-specific" }
}
```

On failure, `data` is omitted and `error` is present:

```json
{
  "schema_version": "1.0",
  "ok": false,
  "error": { "code": "agent_unreachable", "message": "Lore isn't running — ..." }
}
```

- `schema_version` — bumps only on a breaking change to the envelope.
- `ok` — `true` on success, `false` on failure.
- `data` — present on success; mirrors the local API's shape:
  - `search` → `{ results: [memory, …] }`
  - `recent` / `list` → `{ items: [memory, …], total, limit, offset }`
  - `get` → a memory object `{ id, memory, score?, metadata?, created_at?, updated_at? }`
  - `add` → `{ results: [{ id, memory, event }, …] }` (`event` is `ADD`/`UPDATE`/`NONE`)
  - `forget` → `{ forgotten, id?, memory?, remaining_matches }`
  - `status` → `{ version, api_version, components: { agent, memoryd, provider } }`
  - `config get <key>` → `{ key, value }`; `config get` → the whole (scrubbed) config
- `error.code` — a stable code: `agent_unreachable`, `not_found`, `bad_usage`,
  `runtime_error`.

Secrets are never printed — inline key material is redacted at any depth, even in
`--json` (a `*_ref` handle is kept; it is a pointer, not a secret).

`export` is the one exception: it writes the export document **raw** (not wrapped in
the envelope), so `lore export > me.json` produces a clean file.

## Exit codes

A stable, branchable contract for scripts and agents:

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Runtime failure (an unexpected API status) |
| `2` | Bad usage (unknown command, missing argument, bad `--api-url`/`--format`) |
| `3` | Agent unreachable — Lore isn't running |
| `4` | Not found (e.g. `get` on an unknown id) |

When the agent is down, commands print an actionable "Lore isn't running" message and
exit `3` — never a stack trace.

## Examples

```sh
# Is Lore up, and what's ready?
lore status

# Use the user's memory mid-task (machine-readable, branchable):
lore search "preferred database" --json
# → {"schema_version":"1.0","ok":true,"data":{"results":[...]}}

# Branch on the exit code in a script:
if lore status --json >/dev/null 2>&1; then echo "Lore is up"; fi

# Remember and forget:
lore add "I prefer TypeScript over JavaScript" --category preferences
lore forget "JavaScript"

# Change just the model (secrets are routed to the keystore):
lore config set provider.model gpt-4o-mini
lore config set provider.api_key sk-...        # stored securely, never echoed

# Take everything Lore knows about you:
lore export --format json     > me.json
lore export --format markdown > me.md
```

## Connect agent tools

One command installs Lore's portable Agent Skill in the shared user location and the
Claude Code compatibility location. If Claude Desktop is detected, it also safely merges
the MCP shim that desktop client needs.

```sh
lore connect        # detected clients + portable user skill
lore connect --all  # include supported clients that were not detected
```

The older `lore mcp install …` and `lore skills install …` commands remain available
for scripts and manual, protocol-specific setup, but new users should start with
`lore connect`.
