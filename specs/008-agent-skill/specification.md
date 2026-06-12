# 008 — Agent Skill · Specification

> SDD artifact: **what & why.** See [`plan.md`](plan.md) and [`tasks.md`](tasks.md).
> Bound by [`constitution.md`](../../constitution.md). **Depends on:** 007 (the CLI
> the skill drives). Installed by 007's `lore skills install`.

## Overview

The Agent Skill is how skill-capable AI tools *learn* to use Lore. MCP exposes the
tools; the skill teaches an agent **when and why** to reach for them — search memory
before personalizing, record what the user shares, forget on request. It is a
`SKILL.md` package (the emerging Agent Skills standard) that drives the `lore` CLI,
shipped in-repo and installable via `lore skills install`. Plus a set of copy-paste
`docs/integrations/` snippets for tools that read instruction files
(`AGENTS.md`, `CLAUDE.md`) rather than skills.

## User stories

- **As a Claude Code user**, after `lore skills install claude-code`, my agent knows
  to check my Lore memory before making recommendations and to save useful facts I
  mention — without me prompting it each time.
- **As a user of an instruction-file tool**, I paste a Lore snippet into my
  `AGENTS.md`/`CLAUDE.md` and my agent starts using Lore.
- **As the project**, the skill is a low-maintenance, high-leverage distribution
  channel into the skill/plugin ecosystem.

## Scope

### In scope

- **`skills/lore/SKILL.md`**: a well-formed Agent Skill with name, description, and
  guidance on when to `lore search`, `lore add`, `lore forget`, and how to read
  `--json` output — with concrete examples.
- **Trigger guidance**: clear "use this when…" cues so agents invoke Lore at the
  right moments (before personalizing, when the user states a durable preference,
  on explicit forget requests) and avoid over-calling.
- **`docs/integrations/` snippets**: drop-in blocks for `AGENTS.md` / `CLAUDE.md`
  and generic system prompts.
- **Validation**: the skill is exercised end-to-end inside a real skill-capable
  agent (Claude Code) against a running Lore.

### Out of scope

- The CLI itself (007) and its installer command (007 implements
  `skills install`; this spec authors the package it installs).
- The MCP tool surface (006).
- Publishing to external marketplaces (a launch activity, 011 — though the package
  is authored to be publishable).

## Acceptance criteria

1. `skills/lore/SKILL.md` is a valid Agent Skill (correct frontmatter + body) that
   references the `lore` CLI commands accurately.
2. The skill's trigger guidance causes an agent to search Lore before personalizing
   and to record durable user-stated facts — validated in a real Claude Code session.
3. The skill reads `lore ... --json` output per 007's documented schema.
4. `docs/integrations/` contains working `AGENTS.md`/`CLAUDE.md` snippets.
5. `lore skills install claude-code` (007) installs this package and it loads in the
   target agent.

## Non-functional requirements

- **Drives the CLI, adds no behavior** (constitution §8) — the skill is instruction,
  not code.
- **Accurate to the CLI contract**: every command/flag the skill names exists in 007
  with the stated behavior; kept in sync.
- **Portable**: authored to the Agent Skills standard so it works across adopters,
  not just one tool.

## Risks

- *Skill drifts from the CLI as commands evolve.* Mitigation: a test/CI check that
  every command the skill references exists in the CLI's help output.
- *Over-eager invocation annoys users.* Mitigation: explicit "when NOT to use"
  guidance; validate interactively.
