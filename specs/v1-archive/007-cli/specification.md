# 007 — `lore` CLI · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 005 (the API
> it shims).

## Overview

The `lore` CLI makes Lore usable without MCP — from a terminal, a script, CI, or a
coding agent that prefers shelling out to a CLI over a tool protocol (Claude Code,
Codex, aider). It is a small C# console app and a **thin client of the local API**:
human-readable by default, `--json` with a stable schema and meaningful exit codes
for machines. It is also Lore's admin surface (status, config, export) and the home
of the convenience installers that wire Lore into other tools.

## User stories

- **As a coding agent**, I run `lore search "<q>" --json` and get a stable,
  parseable result with a reliable exit code, so I can use the user's memory mid-task.
- **As a power user**, I run `lore status`, `lore recent`, `lore add`, `lore forget`
  from my terminal without opening the app.
- **As a new user**, `lore mcp install claude-desktop` and `lore skills install
  claude-code` wire Lore into my tools without hand-editing config files.
- **As a scripter**, `lore export --format markdown > me.md` gives me my data.

## Scope

### In scope

- **Commands** (thin shims over 005): `search`, `recent`, `add`, `forget`,
  `get`/`list`, `export`, `status`, `config` (get/set, secrets via 004's store).
- **`--json` mode** on every read command: a documented, stable schema; pretty
  human output otherwise.
- **Exit codes**: `0` success, non-zero for distinct failure classes (agent not
  running, bad args, not found), so agents can branch.
- **Installers**: `lore mcp install <client>` (writes the MCP client config, e.g.
  `claude_desktop_config.json`) and `lore skills install <client>` (delegates to
  008's skill package).
- **Agent-not-running UX**: detect when the local API is down and print an
  actionable message (how to start Lore), not a stack trace.

### Out of scope

- The local API itself (005) and the MCP server (006).
- The Agent Skill content (008 — the CLI installs it; 008 authors it).
- The desktop app (010).

## Acceptance criteria

1. `search`, `recent`, `add`, `forget`, `get`/`list`, `export`, `status`, `config`
   all work against a running agent via the local API.
2. Every read command supports `--json` with the documented schema; the schema is
   covered by a contract test.
3. Exit codes distinguish success, agent-unreachable, bad-usage, and not-found;
   documented and tested.
4. `lore mcp install claude-desktop` writes a correct MCP server entry; re-running
   is idempotent.
5. `lore skills install claude-code` installs 008's skill into the right location.
6. With the agent stopped, commands fail with a clear "Lore isn't running" message
   and a non-zero exit code — never a stack trace.

## Non-functional requirements

- **Thin shim:** no behavior beyond calling the local API and formatting output
  (constitution §8).
- **Stable machine contract:** `--json` schema and exit codes are a compatibility
  surface; changes are deliberate and documented.
- **Fast startup:** a CLI invocation feels instant for interactive use.
- **Cross-invocation safety:** never prints secret material, even in `--json`.

## Risks

- *`--json` schema churn breaks agent scripts.* Mitigation: version the schema;
  contract-test it; treat changes as breaking.
- *Installer clobbers a user's existing client config.* Mitigation: merge, don't
  overwrite; idempotent; back up before writing.
