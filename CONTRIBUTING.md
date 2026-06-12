# Contributing to Lore

Thanks for helping build the local-first personal memory layer. Two documents
govern everything here: [constitution.md](constitution.md) (the rules) and
[AGENTS.md](AGENTS.md) (the operating manual). Read both before your first PR —
they are short and they are enforced.

## How work flows: spec-driven development

All work belongs to a spec in [specs/](specs/). Each spec folder has three
artifacts:

- `specification.md` — what and why: user stories, acceptance criteria, scope.
- `plan.md` — the technical approach.
- `tasks.md` — atomic, dependency-ordered tasks, each scoped to one PR.

To contribute: pick (or propose) a spec, work its tasks in order, and keep the
spec updated in the same PR when implementation teaches you something. Tests
are derived from the spec's acceptance criteria and land with the code.

## The delivery gate: `no-mistakes`

Every change ships through the `no-mistakes` gate (constitution §7). No
exceptions, including docs-only changes. Full mechanics live in
[.claude/skills/no-mistakes/SKILL.md](.claude/skills/no-mistakes/SKILL.md).

```sh
# 1. Commit your work on a feature branch (never the default branch).
git switch -c feat/<spec>-<task>
git add -p && git commit

# 2. Run the gate with a complete intent: the goal plus the decisions you made.
no-mistakes axi run --intent "<goal + decisions + tradeoffs>"

# 3. Drive it to a green outcome: resolve auto-fix findings, escalate
#    ask-user findings to a human, and stop only at checks-passed or passed.
```

A task is done when its PR reaches a `checks-passed`/`passed` outcome and CI is
green. CI runs the same checks locally available to you:

| Component | Checks |
|---|---|
| C# | `dotnet format --verify-no-changes` · `dotnet build` (analyzers as errors) · `dotnet test` |
| memoryd | `ruff check` · `ruff format --check` · `mypy` · `pytest` (run from `memoryd/`) |
| app | `npm run lint` · `npm run format:check` · `npm run typecheck` · `npm run build` |
| repo | `gitleaks` secret scan over tree and history |

## Hard rules that save a review round

- **No secrets, ever** — not even "public by design" keys or project URLs.
- **No telemetry, no new outbound calls** without updating `docs/privacy.md`
  in the same PR.
- **Touch external systems only through their seam** (constitution §3).
- **A red linter is a red build** — run the component's checks before pushing.

## Code of conduct

Participation is governed by our [Code of Conduct](CODE_OF_CONDUCT.md)
(Contributor Covenant 2.1). Report unacceptable behavior to the contact listed
there.

## Reporting issues

Bugs and feature requests are welcome as GitHub issues. Security problems go
through [SECURITY.md](SECURITY.md) instead of the public tracker.
