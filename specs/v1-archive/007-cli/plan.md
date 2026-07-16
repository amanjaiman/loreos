# 007 — `lore` CLI · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Build the `cli/` project (skeletoned in 001) into a System.CommandLine app whose
every command is a thin call to the local API (005) plus output formatting. A shared
`LoreApiClient` (or a reuse of 002/005 client types) handles HTTP; a shared output
layer renders either human text or `--json`.

## Structure

```
cli/
├── Lore.Cli.csproj
├── Program.cs              # System.CommandLine root; global --json, --api-url
├── ApiClient.cs           # thin HTTP client of the local API (loopback)
├── Output.cs              # human vs --json rendering; secret scrubbing
├── Commands/
│   ├── SearchCommand.cs · RecentCommand.cs · AddCommand.cs · ForgetCommand.cs
│   ├── GetCommand.cs · ExportCommand.cs · StatusCommand.cs · ConfigCommand.cs
│   └── Install/ McpInstallCommand.cs · SkillsInstallCommand.cs
cli.tests/                  # command + --json schema + exit-code tests
```

## Command surface

```
lore status                         # agent · memoryd · provider health (table; --json)
lore search "<query>" [--limit N] [--json]
lore recent [--limit N] [--json]
lore add "<text>" [--category C]
lore forget "<topic>"
lore get <id> [--json]   ·   lore list [--json]
lore export [--format json|markdown]
lore config get <key>   ·   lore config set <key> <value>   # secrets → 004 store
lore mcp install <claude-desktop|claude-code|cursor>
lore skills install <claude-code|...>
```

Global options: `--json` (machine output), `--api-url` (override loopback target,
default `http://127.0.0.1:7842`).

## `--json` contract & exit codes

- `--json` emits a documented, **versioned** envelope: `{ schema_version, ok, data,
  error }`. Read commands fill `data`; failures fill `error` with a stable `code`.
- Exit codes: `0` success · `1` runtime failure · `2` bad usage · `3` agent
  unreachable · `4` not found. Documented in `docs/integrations/cli.md` and
  asserted in tests.

## Installers

- `mcp install <client>`: locate the client's config (e.g.
  `%APPDATA%\Claude\claude_desktop_config.json`), **merge** a `lore` MCP server
  entry (path to `LoreAgent.exe --mcp`), back up the file first, idempotent.
- `skills install <client>`: copy 008's `skills/lore/` package into the client's
  skills dir (e.g. `~/.claude/skills`), idempotent.

## Agent-not-running UX

`ApiClient` distinguishes "connection refused" from other errors; commands catch it
and print "Lore isn't running — start it with <…>" and exit `3`. No stack traces
reach the user.

## Decisions

- **Thin shim, shared client/output** — zero behavior beyond API calls + formatting.
- **Versioned `--json` envelope** so agent scripts can pin and branch reliably.
- **Installers merge, never overwrite**, and back up — touching a user's tool config
  must be safe and repeatable.
- **The CLI owns installers** (not 006/008) because wiring external tools is an
  admin/UX concern, kept in one place.
- **`recent` shows recent *memories*** (not raw captures), mirroring the MCP
  `get_recent` intent. It is a thin shim over `GET /memories?limit=N` and shares
  that endpoint with `list` (which adds `--offset` paging). The API returns
  memories in the store's order (not a strict recency sort, per
  `IMemoryService.GetRecentAsync`), so "recent" is best-effort until a
  recency-sorted endpoint exists; `recent` and `list` both forward the API's paged
  `{ items, total, limit, offset }` shape under `--json`.

## Dependencies & order

Upstream: **005** (API); `skills install` depends on **008** existing. Internal
order: project + root + ApiClient + Output → read commands (+ `--json`) → write
commands → status/config/export → installers → exit-code + schema tests + docs. See
[`tasks.md`](tasks.md).
