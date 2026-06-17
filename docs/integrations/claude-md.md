# Lore in `CLAUDE.md`

Claude Code loads the [Lore skill](skill.md) automatically once you run
`lore skills install claude-code` — that's the recommended path. But if you'd rather
keep the guidance in your `CLAUDE.md` (for example to scope it to one project, or to
combine it with other house rules), paste the block below.

It carries the same guidance as the skill, condensed. It assumes the
[`lore` CLI](cli.md) is installed and Lore is running.

## Snippet — paste into `CLAUDE.md`

```markdown
## Personal memory (Lore)

The user runs Lore, a local personal-memory tool, exposed through the `lore` CLI.
Use it so your help reflects who this user is.

- **Before a personalized recommendation or opinion**, search their memory:
  `lore search "<topic>" --json --limit 5`. Read `data.results[].memory` and tailor
  your answer to what they already use and prefer.
- **When the user states a durable fact about themselves** (a preference, decision,
  or stable detail), record it: `lore add "<plain statement>"`.
- **When asked to forget something**: `lore forget "<topic>"`.
- **Do not** record secrets or transient details, and don't call `lore` on every
  message — only at the moments above.
- `lore` exits `3` when Lore isn't running. If so, proceed without memory; never block
  on it.
```

## Notes

- All `lore` commands accept `--json`, which prints `{ schema_version, ok, data,
  error }`. Check `ok`, then read `data`. See the [CLI reference](cli.md#the---json-contract).
- If you install the [skill](skill.md), you don't need this snippet — the skill
  surfaces the same guidance to Claude Code on its own.
