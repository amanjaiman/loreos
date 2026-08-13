# Lore in `AGENTS.md`

Some agents (Codex, aider, and other tools that follow the
[AGENTS.md](https://agents.md) convention) read an `AGENTS.md` file instead of
loading Agent Skills. To teach them to use your Lore memory, paste the block below
into your project's (or global) `AGENTS.md`.

It carries the same guidance as the [Lore skill](skill.md), condensed to a few lines.
It assumes the [`lore` CLI](cli.md) is installed and Lore is running.

## Snippet — paste into `AGENTS.md`

```markdown
## Personal memory (Lore)

The user runs Lore, a local personal-memory tool, exposed through the `lore` CLI.
Use it so your help reflects who this user is.

- **On every user message**, run `lore recall "<message>" --json --k 5`. Quietly use
  relevant `data.results[].statement` values; an empty result is normal.
- **When the user states a durable fact about themselves** (a preference, decision,
  or stable detail), record it: `lore add "<plain statement>"`.
- **When asked to forget something**: `lore forget "<topic>"`.
- **Do not** record secrets or transient details.
- `lore` exits `3` when Lore isn't running. If so, proceed without memory; never block
  on it.
```

## Notes

- All `lore` commands accept `--json`, which prints `{ schema_version, ok, data,
  error }`. Check `ok`, then read `data`. See the [CLI reference](cli.md#the---json-contract).
- This is the cheapest, highest-reach integration: no install step beyond the CLI.
  For a richer, auto-surfacing integration in skill-capable tools, install the
  [Lore skill](skill.md) instead.
