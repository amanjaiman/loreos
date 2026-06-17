# skills/

Agent Skill packages ([`SKILL.md`](https://docs.claude.com/en/docs/claude-code/skills))
that teach AI tools how to use Lore.

- [`lore/`](lore/SKILL.md) — teaches a skill-capable agent *when and why* to reach for
  the user's Lore memory (search before personalizing, record durable facts, forget on
  request) by driving the `lore` CLI. Install it with `lore skills install claude-code`
  (spec 007), or see [docs/integrations/skill.md](../docs/integrations/skill.md).

For tools that read instruction files instead of skills (`AGENTS.md`, `CLAUDE.md`),
see the copy-paste snippets in [docs/integrations/](../docs/integrations/).
