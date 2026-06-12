# 001 — Foundation · Plan

> SDD artifact: **technical approach.** Implements the intent in
> [`specification.md`](specification.md); decomposed into PRs in
> [`tasks.md`](tasks.md). Bound by [`constitution.md`](../../constitution.md).

## Approach

Stand up the monorepo as a set of **buildable empty skeletons** wired into one CI
pipeline and the `no-mistakes` gate. Nothing here implements product behavior; the
goal is that every constitutional rule (analyzers-as-errors, gitleaks, no secrets,
documented build) is enforced by machinery before the first feature lands, so
specs 002–011 inherit a known-good, self-policing baseline.

## Target repository structure

```
loreos/
├── global.json                 # pin .NET SDK 8.0.x
├── Lore.sln                    # solution: agent, cli, agent.tests
├── Directory.Build.props       # shared C# props: analyzers, TreatWarningsAsErrors, nullable
├── .editorconfig               # formatting + analyzer severities (C#, TS, py)
├── .gitignore · .gitattributes
├── LICENSE                     # Apache 2.0
├── README.md · CONTRIBUTING.md · SECURITY.md · CODE_OF_CONDUCT.md
├── constitution.md · AGENTS.md  # (already present)
├── .github/
│   ├── workflows/ci.yml        # the PR gate
│   ├── ISSUE_TEMPLATE/ · PULL_REQUEST_TEMPLATE.md
├── agent/                      # LoreAgent.csproj (net8.0-windows) — Program.cs stub
├── cli/                        # Lore.Cli.csproj (net8.0) — Program.cs stub
├── agent.tests/                # xUnit — one passing placeholder test
├── memoryd/
│   ├── pyproject.toml          # ruff + mypy + pytest config
│   ├── lore_memoryd/__init__.py · app.py   # FastAPI app, GET /health
│   └── tests/test_health.py
├── app/                        # electron-forge + React + TS shell → placeholder window
├── skills/                     # (empty; populated by spec 008)
├── docs/
│   ├── architecture.md         # one-behavior-layer / thin-shim overview
│   └── privacy.md              # network-egress section (currently: none)
└── installer/                  # (empty; populated by spec 011)
```

## Component skeletons (minimum buildable)

- **`agent`** — `net8.0-windows`, `UseWPF=true` (needed later for UI Automation;
  set now so the csproj baseline is stable). `Program.cs` prints a version banner
  and exits `0`. No host builder wiring yet (spec 003+).
- **`cli`** — `net8.0` console. `Program.cs` prints `lore <version>` and exits `0`.
- **`agent.tests`** — xUnit project referencing `agent`; one `Assert.True(true)`
  placeholder so `dotnet test` is wired.
- **`memoryd`** — FastAPI app exposing `GET /health → 200 {"status":"ok"}`. No mem0
  import yet (spec 002 pins and adds it). `pyproject.toml` declares fastapi +
  uvicorn + dev tools only.
- **`app`** — electron-forge (TypeScript + Webpack) template trimmed to a single
  placeholder window. No agent spawn, no API calls (spec 010 builds those).

## CI pipeline (`.github/workflows/ci.yml`)

Four required job groups, triggered on PRs to default and on pushes:

| Job | Runner | Steps |
|---|---|---|
| `csharp` | `windows-latest` | setup-dotnet (from `global.json`) · `dotnet format --verify-no-changes` · `dotnet build -warnaserror` · `dotnet test` |
| `python` | `ubuntu-latest` | setup-python 3.11 · `pip install -e memoryd[dev]` · `ruff check` · `mypy` · `pytest` |
| `app` | `ubuntu-latest` | setup-node LTS · `npm ci` · `eslint` · `prettier --check` · `tsc --noEmit` · `npm run build` |
| `secrets` | `ubuntu-latest` | `gitleaks detect` (full tree + history) |

Branch protection requires all four. CI completes in <10 min (skeletons are tiny).

## Tooling configuration

- **C#:** `Directory.Build.props` sets `<Nullable>enable</Nullable>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`,
  `<EnableNETAnalyzers>true</EnableNETAnalyzers>`, `<AnalysisMode>All</AnalysisMode>`.
  `.editorconfig` carries the agreed severities (the one place to tune noise).
- **Python:** `ruff` (lint + format) and `mypy --strict` configured in
  `pyproject.toml`; `pytest` with `-q`.
- **App:** `eslint` (typescript-eslint recommended), `prettier`, `tsc --noEmit`.
- **Secrets:** `.gitleaks.toml` with the default ruleset; no allowlist entries.

## Open-source & docs files

- `LICENSE`: verbatim Apache 2.0 text.
- `README.md`: one-liner, the "what Lore is" paragraph, prerequisites, and a
  build-every-component quickstart that mirrors the table in AGENTS.md. (Demo GIF
  and end-user quickstart are added at launch, spec 011.)
- `SECURITY.md`: how to report vulnerabilities; restate the no-secrets / localhost
  / zero-telemetry posture.
- `CONTRIBUTING.md`: the SDD loop, the `no-mistakes` gate requirement, and a
  pointer to `AGENTS.md` + `constitution.md`.
- `docs/architecture.md`: the §3 invariants in narrative form (carry the diagram
  from the project plan).
- `docs/privacy.md`: "Network egress" section stating the only outbound calls are
  to user-configured model endpoints (currently zero, since no provider exists
  yet). This file grows with specs 002/004.

## `no-mistakes` integration

`no-mistakes init` is run once and its config committed. From here on, **every
task in every spec** ends by taking its PR through the gate (constitution §7).
This spec's own tasks are the first to exercise it end-to-end.

## Dependencies & order

No upstream spec. Internally, the natural order is: solution + props → component
skeletons → tooling config → CI → OSS/docs files → `no-mistakes init` → verify the
gate. See [`tasks.md`](tasks.md).

## Decisions

- **Empty-but-buildable over stubbed-with-TODOs.** Skeletons compile and pass a
  trivial test; they contain no placeholder business logic that a later spec would
  have to unpick.
- **`UseWPF=true` set now.** Avoids a disruptive csproj change mid-capture-spec.
- **C# on Windows runners, everything else on Linux.** Matches the Windows-first
  reality without paying Windows-runner cost on Python/app/secret jobs.
