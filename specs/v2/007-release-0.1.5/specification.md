# v2-007 — Release 0.1.5 polish · Specification

> Bound by [`constitution.md`](../../../constitution.md). This release-sized spec repairs
> shipped behavior; it does not add a new behavior layer.

## Goal

Make Lore feel like one product on Windows: its window carries Lore's icon, privacy
choices take effect and persist immediately, Settings contains no redundant appearance
page, and connecting agent tools is one provider-neutral action.

## Requirements

1. Lore's installed window uses the Lore taskbar icon, including after an upgrade from a
   build whose Electron icon may be cached.
2. Adding or removing a blocklist item persists without a separate Save action and updates
   the running filter before the next capture. Pause/resume also affects the live loop.
3. Settings has no Appearance section; the rail remains the theme control.
4. `lore connect` installs the Lore Agent Skill at user scope for shared-standard clients
   and Claude Code compatibility, and configures detected Claude Desktop through MCP.
5. Connections presents one recommended agent action and keeps MCP as an advanced surface.
6. Existing `lore mcp install` and `lore skills install` commands remain compatible.
7. The bundled skill follows v2-001's every-turn `lore recall` contract.

## Out of scope

- A hosted registry, marketplace, remote MCP server, or silent configuration during app update.
- Removing legacy CLI commands or implementing bespoke support for every agent vendor.
- Theme changes, new capture filters, or unrelated Settings redesign.
