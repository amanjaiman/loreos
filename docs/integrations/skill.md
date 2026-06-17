# The Lore Agent Skill

The Lore **Agent Skill** teaches a skill-capable agent (Claude Code, and other
adopters of the [Agent Skills](https://docs.claude.com/en/docs/claude-code/skills)
standard) *when and why* to use your Lore memory — search before personalizing,
record durable facts you mention, forget on request — without you prompting it each
time. It's instruction, not code: it drives the [`lore` CLI](cli.md), which already
handles `--json`, exit codes, and the "Lore isn't running" case.

The package lives in-repo at [`skills/lore/`](../../skills/lore/SKILL.md).

## Install

The easy way (spec 007's installer):

```sh
lore skills install claude-code
```

This copies the package into Claude Code's user skills directory
(`~/.claude/skills/lore`). Restart Claude Code (or reload its skills) and it will pick
up the `lore` skill. Re-running the command is safe — it just refreshes the files.

### Manual install

If you'd rather copy it yourself, place the `skills/lore/` folder under your tool's
skills directory. For Claude Code that's:

```
%USERPROFILE%\.claude\skills\lore\SKILL.md
```

Any tool that reads the Agent Skills format can load the same package.

## What it does

Once loaded, the skill nudges the agent to:

- **Search before personalizing** — run `lore search "<topic>" --json` before
  recommending tools, libraries, or approaches, and tailor the answer to what you
  already use.
- **Record durable facts** — when you state a lasting preference, decision, or detail
  about yourself, save it with `lore add "<fact>"`.
- **Forget on request** — `lore forget "<topic>"` when you ask it to.
- **Stay out of the way** — it won't record secrets or throwaway details, and it
  proceeds normally if Lore isn't running (the CLI exits `3`).

## Prerequisites

- **Lore is installed and set up** (a model provider chosen in onboarding) and
  running, so the local API on `127.0.0.1:7842` is up. Check with `lore status`.
- The **`lore` CLI** is on your `PATH`.

## Not using a skill-capable tool?

Paste a short guidance block into your instruction file instead:

- [`AGENTS.md` snippet](agents-md.md)
- [`CLAUDE.md` snippet](claude-md.md)
