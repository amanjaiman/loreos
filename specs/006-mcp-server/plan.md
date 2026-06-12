# 006 — MCP Server · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Port v1's `LoreTools` (the `[McpServerTool]` definitions) but re-back each tool on
`IMemoryService` / the local API instead of v1's SQLite repositories. One tool
implementation serves both transports. Drop the two bucket tools. Keep names and
descriptions stable.

## Structure

```
agent/Mcp/
├── LoreTools.cs        # the 7 tool definitions ([McpServerTool(Name=...)] + [Description])
├── McpStdioServer.cs   # --mcp entrypoint: DI graph (002) without the HTTP listener
└── McpHttpTransport.cs # Streamable HTTP transport mounted on the local API (005)
docs/integrations/
├── claude-desktop.md · claude-code.md · cursor.md · http-clients.md
```

## Tools (re-backed on `IMemoryService`)

| Tool | Maps to |
|---|---|
| `get_context(query, limit)` | `Search` |
| `get_recent(limit)` | `GetRecent` |
| `get_profile()` | `GetAll` grouped by category metadata |
| `summarize_profile()` | `GetAll` → natural-language summary (via the user's provider) |
| `add_context(text, category)` | `Remember` |
| `update_context(topic, newInfo)` | `Search` + `Update` |
| `forget(topic)` | `Search` + `Delete` |

Descriptions are ported verbatim from v1 (they were well-tuned), then lightly
refined. `summarize_profile` is the only tool that calls a model (through
`IInferenceBackend`), exactly as in v1.

## Transports

- **stdio:** `LoreAgent --mcp` builds the 002 DI graph, registers `LoreTools`, and
  runs the stdio JSON-RPC server — **no** Kestrel/HTTP listener (constitution §3.3;
  stdout is owned by the MCP transport).
- **Streamable HTTP:** the same `LoreTools` mounted as a Streamable HTTP endpoint on
  the loopback API (005), for clients that prefer a URL.

`McpServerToolAttribute` uses the named form (`[McpServerTool(Name = "...")]`) — a
carried-over v1 gotcha worth keeping in the plan.

## Naming sanity check

Before finalizing, compare tool names against OpenMemory MCP conventions; keep
Lore's names (for v1 compatibility) but note any alias worth documenting so users
of both feel at home.

## Docs

`docs/integrations/` gets one file per client with copy-paste config and a
verification step ("you should see a green indicator / the tool listed"). These
double as launch material.

## Decisions

- **Drop bucket tools** (buckets deferred) — keeps the surface honest about what
  exists.
- **One tool impl, two transports** — no logic divergence.
- **Installers live in the CLI (007), not here** — this spec is the server; writing
  client config files is a CLI convenience.

## Dependencies & order

Upstream: **005** (API), **002** (`IMemoryService`), **004** (for
`summarize_profile`). Internal order: port tools on `IMemoryService` → stdio mode →
Streamable HTTP → naming check + docs → integration test. See [`tasks.md`](tasks.md).
