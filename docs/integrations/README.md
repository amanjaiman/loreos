# Connecting Lore to your AI tools (MCP)

Lore speaks the [Model Context Protocol](https://modelcontextprotocol.io). Any
MCP-capable client — Claude Desktop, Claude Code, Cursor, and others — can read and
write your personal memory through the same seven tools. Lore captures context
ambiently; these integrations let your assistant *use* it.

## Two transports, same tools

| Transport | When to use | Endpoint |
|---|---|---|
| **stdio** | The client launches Lore as a child process (the usual desktop setup). | `LoreAgent.exe --mcp` |
| **Streamable HTTP** | The client connects to a URL (Lore is already running). | `http://127.0.0.1:7842/mcp` |

Both expose the **same seven tools** and read the **same memory** — pick whichever
your client supports. Both are **loopback-only**: nothing is ever reachable off your
machine (constitution §3.4).

## The tools

| Tool | What it does |
|---|---|
| `get_context(query, limit?)` | Search your memory for context relevant to a query. |
| `get_recent(limit?)` | The most recent things Lore has recorded, no query needed. |
| `get_profile()` | Everything Lore knows, grouped by category. |
| `summarize_profile()` | A short natural-language summary of who you are and what you're working on. |
| `add_context(text, category?)` | Record a new durable fact, preference, or decision. |
| `update_context(topic, newInfo)` | Replace what Lore remembers about a topic (adds it fresh if nothing matches). |
| `forget(topic)` | Delete the memory most relevant to a topic. |

These names are stable: they match Lore v1, so an existing config keeps working.

## Guides

- [Claude Desktop](claude-desktop.md)
- [Claude Code](claude-code.md)
- [Cursor](cursor.md)
- [Generic Streamable HTTP clients](http-clients.md) — and a note for users coming
  from OpenMemory MCP.
- [The `lore` CLI](cli.md) — terminal/script access, the `--json` contract, exit
  codes, and the `mcp install` / `skills install` installers.
- [The Lore Agent Skill](skill.md) — teach a skill-capable agent (Claude Code) to use
  your memory automatically. For instruction-file tools, drop in the
  [`AGENTS.md`](agents-md.md) or [`CLAUDE.md`](claude-md.md) snippet.

## Prerequisites

- **Lore is installed and set up** (you've chosen a model provider in onboarding).
  The tools call your own model and memory; nothing is sent to a Lore-operated
  server.
- For **stdio**, you need the path to `LoreAgent.exe`. The examples use
  `C:\Program Files\Lore\LoreAgent.exe` — adjust it to your install location.
- For **Streamable HTTP**, the Lore app (or agent) must be **running**, so the local
  API on `127.0.0.1:7842` is up.

> Prefer not to hand-edit JSON? `lore mcp install <claude-desktop|claude-code|cursor>`
> writes these client configs for you — it merges, never overwrites, and backs up
> first. See [the CLI guide](cli.md#installers).
