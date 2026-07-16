# 008 — Agent Skill · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. **Every task ends at a green
> `no-mistakes` gate on a feature branch** (constitution §7) — implicit in every
> "Done when". `[deps: …]` lists prerequisites.

---

### T001 — Author `skills/lore/SKILL.md`  `[deps: spec 007]`
Write the skill: frontmatter (`name: lore`, tuned description, `user-invocable`),
when-to-use / when-not-to-use triggers, exact `lore` commands with examples, and how
to read the `--json` envelope and exit codes.
**Done when:** `SKILL.md` is valid and every command/flag it names matches 007's CLI
(acceptance criteria 1, 3).

### T002 — Instruction-file snippets  `[deps: T001]`
Write `docs/integrations/{agents-md,claude-md,skill}.md` — copy-paste blocks
carrying the same when/how guidance for tools that read instruction files.
**Done when:** snippets are accurate and self-contained (acceptance criterion 4).

### T003 — Interactive validation  `[deps: T001, spec 007]`
Load the skill in Claude Code against a running Lore; confirm it searches before
personalizing and records durable user-stated facts; tune wording from observed
behavior.
**Done when:** the documented behaviors are observed and the validation is recorded
in the spec folder (acceptance criteria 2, 5).

### T004 — Drift check  `[deps: T001]`
Add a CI/test step that parses the command names referenced in `SKILL.md` and
asserts each exists in `lore --help`.
**Done when:** the check passes and fails loudly if the skill names a non-existent
command (non-functional: accuracy to the CLI contract).

---

## Definition of done for spec 008

The Lore skill teaches a real agent to use Lore at the right moments via the CLI;
instruction-file snippets cover non-skill tools; a drift check keeps the skill honest
against 007. Skill-capable tools now know how to use Lore.
