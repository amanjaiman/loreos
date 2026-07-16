# 008 — Agent Skill · Plan

> SDD artifact: **technical approach.** Implements [`specification.md`](specification.md);
> PRs in [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Author a `SKILL.md` package that teaches an agent to use the `lore` CLI well, plus
instruction-file snippets for non-skill tools. The artifact is prose + examples, not
code — its quality is judged by how an agent behaves with it loaded, so validation is
interactive against a real agent.

## Structure

```
skills/lore/
└── SKILL.md            # frontmatter (name, description, user-invocable) + guidance
docs/integrations/
├── agents-md.md        # drop-in AGENTS.md snippet
├── claude-md.md        # drop-in CLAUDE.md snippet
└── skill.md            # how the skill works + manual install
```

## SKILL.md content

- **Frontmatter**: `name: lore`, a description tuned so the host agent surfaces it at
  the right time, `user-invocable: true`.
- **When to use** (triggers): before giving a personalized recommendation; when the
  user states a durable preference, decision, or fact about themselves; when the
  user asks "what do you know about me?"; when the user says to forget something.
- **When *not* to use**: transient/throwaway details, secrets, anything the user
  wouldn't want persisted.
- **How to use**: the exact `lore` commands with examples —
  `lore search "<topic>" --json --limit 5` before personalizing,
  `lore add "<durable fact>"` to record, `lore forget "<topic>"` on request — and
  how to read the `{schema_version, ok, data, error}` envelope (007).
- **Failure handling**: if `lore` exits `3` (agent not running), proceed without
  memory and optionally tell the user Lore isn't running.

## Instruction-file snippets

Short, copy-paste blocks for `AGENTS.md` / `CLAUDE.md` that capture the same
when/how guidance in a few lines, for tools that read instruction files rather than
skills. These are the cheapest, highest-reach integration.

## Validation & drift control

- **Interactive validation**: load the skill in Claude Code against a running Lore;
  confirm it searches before personalizing and records durable facts (acceptance
  criterion 2).
- **Drift check**: a CI/test step parses the command names referenced in `SKILL.md`
  and asserts each exists in `lore --help`, so the skill can't silently diverge from
  007.

## Decisions

- **Skill drives the CLI, not the API directly** — one integration path for agents
  to learn, and the CLI already handles `--json`/exit codes/agent-down.
- **Author to the portable Agent Skills standard** so the same package works across
  adopters; installation into a specific tool is 007's job.
- **Snippets ship alongside the skill** — together they cover both skill-capable and
  instruction-file tools.

## Dependencies & order

Upstream: **007** (the CLI + its `--json` contract; `skills install` consumes this
package). Internal order: author `SKILL.md` → snippets → interactive validation →
drift check. See [`tasks.md`](tasks.md).
