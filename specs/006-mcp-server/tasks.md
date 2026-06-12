# 006 — MCP Server · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — Port the 7 tools onto `IMemoryService`  `[deps: spec 005]`
Port `LoreTools` with the seven core tools re-backed on `IMemoryService` / the local
API; drop the two bucket tools; carry descriptions over from v1.
**Done when:** each tool maps to the documented service call and returns the right
shape; unit tests cover the mappings (acceptance criterion 1 logic).

### T002 — stdio server (`LoreAgent --mcp`)  `[deps: T001]`
Implement `McpStdioServer`: build the 002 DI graph, register the tools, run stdio
JSON-RPC, and ensure **no** HTTP listener opens in this mode.
**Done when:** `LoreAgent --mcp` serves all seven tools over stdio against a seeded
store; a check confirms the HTTP API is not listening (acceptance criteria 1, 3).

### T003 — Streamable HTTP transport  `[deps: T001, spec 005]`
Mount the same tools as a Streamable HTTP endpoint on the loopback local API.
**Done when:** all seven tools work over Streamable HTTP, loopback-only
(acceptance criterion 2).

### T004 — Naming check + integration docs  `[deps: T002]`
Sanity-check tool names against OpenMemory MCP conventions (keep v1 names for
compatibility; note any useful alias). Write `docs/integrations/{claude-desktop,
claude-code,cursor,http-clients}.md`.
**Done when:** docs contain working, verified setup steps for each client
(acceptance criteria 4, 5).

### T005 — MCP round-trip integration test  `[deps: T002, T003]`
Add an integration test driving an MCP client through search → add → forget against
a seeded store, over both transports.
**Done when:** the round-trip passes on stdio and Streamable HTTP (acceptance
criterion 6).

---

## Definition of done for spec 006

All seven tools work over both transports as thin shims over the local API; v1
client configs still connect; integration docs are real; the round-trip test is
green. Lore is reachable from every MCP client.
