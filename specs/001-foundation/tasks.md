# 001 — Foundation · Tasks

> SDD artifact: **atomic work units.** Each task is one PR (~1–4 h), ordered by
> dependency. **Every task ends the same way** (constitution §7): commit on a
> feature branch, then drive `no-mistakes axi run --intent "<task goal + decisions>"`
> to a `checks-passed`/`passed` outcome. That step is implicit in every "Done when"
> below and is not repeated each time.

Legend: `[deps: …]` lists prerequisite task IDs.

---

### T001 — Solution skeleton + shared C# build props  `[deps: —]`
Create `Lore.sln`, `global.json` (pin .NET 8.0.x), and `Directory.Build.props`
(nullable enable, `TreatWarningsAsErrors`, NET analyzers, `AnalysisLevel=8.0`,
`AnalysisMode=All`, `EnforceCodeStyleInBuild=true`). Add
empty `agent` (`net8.0-windows`, `UseWPF=true`), `cli` (`net8.0`), and
`agent.tests` (xUnit) projects with `Program.cs`/test stubs.
**Done when:** `dotnet build` is warning-clean and `dotnet test` passes the
placeholder test.

### T002 — Editor & formatting config  `[deps: T001]`
Add `.editorconfig` (C#/TS/Python severities — the single place to tune analyzer
noise), `.gitattributes`, and confirm `dotnet format --verify-no-changes` passes.
**Done when:** `dotnet format --verify-no-changes` exits clean on the solution.

### T003 — `memoryd` Python skeleton  `[deps: —]`
Create `memoryd/pyproject.toml` (fastapi, uvicorn; dev: ruff, mypy, pytest),
`lore_memoryd/app.py` with `GET /health → {"status":"ok"}`, and
`tests/test_health.py`. **Do not** add mem0 yet (spec 002 owns the pin).
**Done when:** `pip install -e memoryd[dev]`, `ruff check`, `mypy`, and `pytest`
all pass; `/health` returns 200.

### T004 — Electron + React app shell  `[deps: —]`
Scaffold `app/` with electron-forge (TypeScript + Webpack), trimmed to a single
placeholder window. Configure eslint, prettier, `tsc --noEmit`. No agent spawn, no
API calls.
**Done when:** `npm ci`, lint, `tsc --noEmit`, and `npm run build` succeed; `npm
start` opens the window.

### T005 — Repo skeleton & directory placeholders  `[deps: —]`
Create remaining top-level dirs (`skills/`, `installer/`, `docs/`) with
`.gitkeep`/README placeholders, and `.gitignore` covering .NET, Python, Node, and
Electron build artifacts.
**Done when:** tree matches `plan.md`; `git status` is clean after a build (no
artifacts tracked).

### T006 — Licensing & front-door docs  `[deps: —]`
Add `LICENSE` (Apache 2.0), `README.md` (one-liner + "what Lore is" + prerequisites
+ build-every-component quickstart), `CONTRIBUTING.md` (SDD loop + gate +
pointers), `SECURITY.md`, `CODE_OF_CONDUCT.md`.
**Done when:** README quickstart commands match the real build commands; links
resolve.

### T007 — Seed `docs/architecture.md` and `docs/privacy.md`  `[deps: T006]`
Write `architecture.md` (one-behavior-layer / thin-shim invariants + diagram from
the project plan) and `privacy.md` with a "Network egress" section stating zero
non-user-configured outbound calls.
**Done when:** both docs exist and reflect constitution §3–§4; privacy egress list
is accurate for the current (behavior-free) tree.

### T008 — CI pipeline  `[deps: T001–T004]`
Add `.github/workflows/ci.yml` with the four required job groups (csharp on
windows-latest; python, app, gitleaks on ubuntu-latest) per `plan.md`. Add
`.gitleaks.toml` (default rules, no allowlist), issue/PR templates.
**Done when:** CI runs green on the PR; a temporary planted lint error fails the
relevant job and a planted fake secret fails `gitleaks` (revert both before merge).

### T009 — Initialize the delivery gate  `[deps: T001, T008]`
Run `no-mistakes init`; commit its config. Document the gate workflow in
`CONTRIBUTING.md`. Configure default-branch protection requiring the four CI jobs.
**Done when:** a trivial change is driven through `no-mistakes` to `checks-passed`
on a feature branch, proving the gate end-to-end.

---

## Definition of done for spec 001

All of `specification.md`'s acceptance criteria pass on a clean Windows checkout,
CI is required and green on the default branch, `gitleaks` is clean, and the
`no-mistakes` gate has been exercised successfully at least once. Specs 002–011 can
now begin.
