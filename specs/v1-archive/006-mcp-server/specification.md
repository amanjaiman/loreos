# 006 — MCP Server · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 005 (the API
> it shims), 002 (`IMemoryService`).

## Overview

The MCP server is Lore's first and most important integration surface: it lets any
MCP-speaking AI tool — Claude Desktop, Claude Code, Cursor, ChatGPT connectors,
Gemini — read and write the user's memory. It is a **thin shim** (constitution §8)
that exposes the local API's behavior as MCP tools. This spec re-backs v1's
well-designed tool surface onto `IMemoryService`, keeps the tool names and
descriptions stable for drop-in compatibility, and adds Streamable HTTP alongside
stdio so both local and URL-based clients work.

## User stories

- **As a Claude Desktop user**, I add Lore once and Claude can recall what I've been
  doing and record what I tell it — using memory Lore captured ambiently.
- **As a user of any MCP client**, the same tools work whether my client wants stdio
  or a Streamable HTTP URL.
- **As a v1 user**, my existing `claude_desktop_config.json` keeps working because
  the tool names didn't change.

## Scope

### In scope

- **Seven core tools**, re-backed on `IMemoryService` / the local API:
  `get_context`, `get_recent`, `get_profile`, `summarize_profile`, `add_context`,
  `update_context`, `forget`. (Bucket tools from v1 are **dropped** — buckets are
  deferred.)
- **Two transports:** stdio (`LoreAgent --mcp`, reusing the 002 DI graph without the
  HTTP listener) and **Streamable HTTP** on the local API for URL-based clients.
- **Tool descriptions** carried over and refined — these are the model-facing
  contract and v1's were good.
- **Naming sanity check** against OpenMemory MCP conventions for ecosystem
  familiarity.
- **Docs**: `docs/integrations/` setup guides for Claude Desktop, Claude Code,
  Cursor, and generic Streamable HTTP clients.

### Out of scope

- `lore mcp install` config-writing convenience (007 — the CLI owns installers).
- The local API and `IMemoryService` (005/002).
- Buckets/retention tools (deferred).

## Acceptance criteria

1. All seven tools work over stdio against a seeded store and return useful,
   correctly-shaped results.
2. The same seven tools work over Streamable HTTP.
3. `LoreAgent --mcp` starts an stdio server that reuses the agent DI graph and does
   **not** open the HTTP API listener.
4. Tool names match v1 (minus bucket tools), so an existing Claude Desktop config
   connects without edits.
5. `docs/integrations/` contains working setup instructions for at least Claude
   Desktop, Claude Code, Cursor, and a generic HTTP client.
6. An integration test performs an MCP client round-trip (search + add + forget)
   against a seeded store.

## Non-functional requirements

- **Thin shim:** tools contain no business logic beyond translating to/from the
  local API / `IMemoryService` (constitution §8).
- **Loopback only** for the HTTP transport (constitution §3.4).
- **Stable contract:** tool names/shapes are a compatibility surface; changes are
  deliberate and documented.

## Risks

- *Tool descriptions drive model behavior.* Mitigation: carry v1's proven wording;
  review changes as behavior changes.
- *Transport differences (stdio vs HTTP) cause subtle bugs.* Mitigation: one tool
  implementation behind both transports; test both.
