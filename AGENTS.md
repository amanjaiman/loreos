# AGENTS.md — Working in the Lore (loreos) Repo

This file is the operating manual for any agent (or human) picking up a spec. Read
it before you touch code. It tells you *how* to work here; [`constitution.md`](constitution.md)
tells you *why* and sets the rules you must not break.

---

## What Lore is (30 seconds)

Lore is the open-source **ambient capture agent for a personal memory layer**. A
C# agent watches the active window on Windows, distills what matters with the
user's own model, stores it in [mem0](https://github.com/mem0ai/mem0) (via a
bundled Python sidecar), and serves it to AI tools through MCP, a CLI, an agent
skill, and a REST API. Local-first, zero-telemetry, bring-your-own-model.

---

## Repository layout

```
loreos/
├── constitution.md      # the rules — read first, non-negotiable
├── AGENTS.md            # this file
├── specs/               # SDD specs — specs/v2 is active; specs/v1-archive is provenance
│   ├── NNN-name/        #   v1 specs: specification.md · plan.md · tasks.md
│   └── v2/              #   v2 restart spec set — see specs/v2/README.md
├── agent/               # C# capture agent + MCP server + local API host
├── cli/                 # `lore` CLI (C#)
├── memoryd/             # Python FastAPI wrapper around mem0
├── app/                 # Electron + React desktop app
├── skills/              # Agent Skill packages (SKILL.md)
├── docs/                # architecture.md, privacy.md, integrations/, ...
└── installer/           # packaging
```

Components and their build/run commands are introduced by the spec that creates
them. Until a component exists, its directory may be empty — that is expected
while specs are still being implemented in dependency order.

---

## The SDD loop you must follow

For the spec you are assigned (`specs/NNN-name/`):

1. **Read** `constitution.md`, then this file, then the spec's three files
   (`specification.md` → `plan.md` → `tasks.md`).
2. **Confirm scope.** The spec's "Out of scope" section is binding. Do not pull in
   work that belongs to another spec — note it and move on.
3. **Work tasks in order.** Each task in `tasks.md` is one PR. Implement it, write
   its tests (derived from the acceptance criteria), and keep the spec in sync if
   you learn something that changes it.
4. **Gate every task.** Commit on a feature branch and run `no-mistakes` (below)
   before considering the task done.
5. **Stop at the spec's boundary.** When the tasks are done and gated, report
   outcomes; do not drift into the next spec.

---

## The delivery gate (`no-mistakes`)

Every task ships through the gate. The short version:

```sh
# 1. Be on a feature branch with your work committed (NOT the default branch).
git switch -c feat/<spec>-<task>
git add -p && git commit -m "..."

# 2. Run the gate, passing the real intent (the goal + decisions you made).
no-mistakes axi run --intent "<what this task set out to accomplish, with tradeoffs>"

# 3. Drive it: resolve auto-fix / no-op findings yourself; ESCALATE ask-user
#    findings to a human verbatim. Done at outcome `checks-passed` or `passed`.
```

Full mechanics live in `.claude/skills/no-mistakes/SKILL.md`. Key rules: the gate
validates committed history (commit first), `--intent` is required and must be
complete, and you never hand-edit code to fix a finding while a run is active — the
pipeline owns the fix.

### Get it green locally BEFORE you invoke the gate — this is the dev-loop speed lever

Every gate run executes review → test → lint → push → PR → **CI**, and **each fix
round re-runs the whole thing**, including a fresh CI cycle on GitHub Actions (the
Windows runner alone costs 1–2 min to provision before any job starts). A task that
trips the gate three times pays that cost three times. The cheapest run is the one
that finds nothing.

So before `git commit` + `axi run`, run the **same checks CI runs**, locally, for
every component you touched, and don't invoke the gate until they're all green:

```sh
# C# (agent / cli)
dotnet format --verify-no-changes   # formatting — a top source of avoidable fix rounds
dotnet build -warnaserror           # analyzers-as-errors
dotnet test
# Python (memoryd)
ruff check . && mypy . && pytest -q
# App
npm --prefix app run lint && npx --prefix app tsc --noEmit && npm --prefix app run build
```

This single habit is what keeps the gate a quick rubber-stamp instead of a
multi-minute bounce. Treat a gate fix-round as a signal you skipped a local check.

### Prefer Bash for routine git/file ops

The default shell here is Windows PowerShell, which pays profile-load startup on
every invocation. For routine `git`, file, and check commands, prefer the **Bash**
tool — it's faster per call and the POSIX one-liners above run as-is. Reserve long
timeouts for the genuinely long calls (`no-mistakes axi run` / `respond`, which
block for minutes on review/test/CI — that wait is normal, don't cancel it).

---

## Build & run quick reference

> Filled in per component as specs land. The canonical commands always live in the
> component's spec `plan.md`; this is the cheat sheet.

| Component | Build | Test |
|---|---|---|
| `agent` | `dotnet build agent` | `dotnet test agent.tests` |
| `cli` | `dotnet build cli` | *(no test project yet; arrives with spec 007)* |
| `memoryd` | `pip install -e "memoryd[dev]"` | `pytest memoryd` |
| `app` | `npm --prefix app ci && npm --prefix app run build` | *(no tests yet; app behavior arrives with spec 010)* |

Run the agent: `LoreAgent.exe` (agent mode, local API on :7842) or
`LoreAgent.exe --mcp` (MCP stdio mode).

---

## Conventions that will save you a review round

- **Touch external systems only through their seam.** mem0 → `IMemoryService`.
  Models → `IInferenceBackend`. Win32/UIA → the capture extractor interfaces.
  Adding an HTTP call anywhere else will be rejected.
- **No secrets in code or config.** User keys go in Windows Credential Manager and
  are referenced by handle. `gitleaks` will catch you; review will too.
- **No telemetry. No new outbound calls** without updating
  [docs/privacy.md](docs/privacy.md) in the same PR.
- **No business layer.** Auth, accounts, cloud sync, analytics, hosted inference,
  and MCP-key management from `lore/v1` are deleted, not ported. If a task seems to
  need them, you have misread the spec.
- **Format and analyzers are gates.** Run the full local check suite before you
  commit (see "Get it green locally" above) — drift fails CI and costs you a whole
  gate fix-round.
- **Tests come from acceptance criteria.** If a behavior is in the spec's
  acceptance list, it has a test.

---

## When something is ambiguous

Specs aim to be unambiguous, but if you hit a genuine fork the spec does not
resolve: prefer the option most consistent with the constitution's principles,
implement it, and record the decision in the spec (`plan.md`) so the next agent
inherits it. If the fork is a product-behavior call rather than an implementation
detail, surface it to a human rather than guessing — the same way a `no-mistakes`
`ask-user` finding is escalated, not self-resolved.
