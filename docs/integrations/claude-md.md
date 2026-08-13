# Lore in `CLAUDE.md`

Claude Code loads the [Lore skill](skill.md) automatically once you run
`lore connect` — that's the recommended path. But if you'd rather
keep the guidance in your `CLAUDE.md` (for example to scope it to one project, or to
combine it with other house rules), paste the block below.

It carries the same guidance as the skill, condensed. It assumes the
[`lore` CLI](cli.md) is installed and Lore is running.

## Snippet — paste into `CLAUDE.md`

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
- If you install the [skill](skill.md), you don't need this snippet — the skill
  surfaces the same guidance to Claude Code on its own.
