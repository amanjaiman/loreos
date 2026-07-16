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
├── global.json                 # pin .NET SDK 8.0.204; rollForward latestPatch
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
├── agent.tests/                # xUnit (net8.0-windows, UseWPF=true) — one passing placeholder test
├── memoryd/
│   ├── pyproject.toml          # ruff + mypy + pytest config
│   ├── lore_memoryd/__init__.py · app.py   # FastAPI app, GET /health
│   └── tests/test_health.py
├── app/                        # electron-forge + React + TS shell → placeholder window
├── skills/                     # README placeholder; populated by spec 008
├── docs/
│   ├── architecture.md         # one-behavior-layer / thin-shim overview
│   └── privacy.md              # network-egress section (currently: none)
└── installer/                  # README placeholder; populated by spec 011
```

## Component skeletons (minimum buildable)

- **`agent`** — `net8.0-windows`, `UseWPF=true` (needed later for UI Automation;
  set now so the csproj baseline is stable). `Program.cs` prints a version banner
  and exits `0`. No host builder wiring yet (spec 003+).
- **`cli`** — `net8.0` console. `Program.cs` prints `lore <version>` and exits `0`.
- **`agent.tests`** — `net8.0-windows`, `UseWPF=true` (must match the agent's TFM
  so the project reference resolves). xUnit project referencing `agent`; one
  `Assert.True(true)` placeholder so `dotnet test` is wired.
- **`memoryd`** — FastAPI app exposing `GET /health → 200 {"status":"ok"}`. No mem0
  import yet (spec 002 pins and adds it). `pyproject.toml` declares fastapi +
  uvicorn + dev tools only.
- **`app`** — electron-forge (TypeScript + Webpack) template trimmed to a single
  placeholder window. No agent spawn, no API calls (spec 010 builds those).

## CI pipeline (`.github/workflows/ci.yml`)

Four required job groups, triggered on PRs to default and on pushes:

| Job       | Runner           | Steps                                                                                                                 |
| --------- | ---------------- | --------------------------------------------------------------------------------------------------------------------- |
| `csharp`  | `windows-latest` | setup-dotnet (from `global.json`) · `dotnet format --verify-no-changes` · `dotnet build -warnaserror` · `dotnet test` |
| `python`  | `ubuntu-latest`  | setup-python 3.11 · `pip install -e memoryd[dev]` · `ruff check` · `mypy` · `pytest`                                  |
| `app`     | `ubuntu-latest`  | setup-node LTS · `npm ci` · `eslint` · `prettier --check` · `tsc --noEmit` · `npm run build`                          |
| `secrets` | `ubuntu-latest`  | `gitleaks detect` (full tree + history)                                                                               |

All four checks are required on PRs. Branch protection enforcing them is deferred to spec 011 (GitHub blocks it on free private repos; see T009 deviation note). CI completes in <10 min (skeletons are tiny).

## Tooling configuration

- **C#:** `Directory.Build.props` sets `<LangVersion>latest</LangVersion>`,
  `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`,
  `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`,
  `<EnableNETAnalyzers>true</EnableNETAnalyzers>`, `<AnalysisLevel>8.0</AnalysisLevel>`,
  `<AnalysisMode>All</AnalysisMode>`, `<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>`
  (pinned to 8.0 so the analyzer ruleset is deterministic across SDK patch versions).
  `.editorconfig` is the single place to tune analyzer noise — never `<NoWarn>` in
  project files. Agreed baseline (see Decisions below): file-scoped namespaces and
  required braces at `warning` (become build errors via `EnforceCodeStyleInBuild` +
  `TreatWarningsAsErrors`); interface prefix naming at `warning`; CA1303 and CA1014
  disabled; CA1848 and CA2254 downgraded to `suggestion` until logging arrives in
  spec 003.
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
- **Analyzer severities live in `.editorconfig`, never `<NoWarn>`.** Keeps the
  agreed noise baseline in one visible, reviewable place rather than scattered
  across project files.
- **File-scoped namespaces and required braces at `warning`.** Both become build
  errors via `EnforceCodeStyleInBuild` + `TreatWarningsAsErrors`, enforcing
  consistent style without a separate linter pass.
- **Interface prefix naming at `warning`.** Same escalation path as above;
  `IFoo` convention is non-negotiable for this codebase.
- **CA1303 (localized strings) disabled.** Lore is deliberately not localized;
  the rule adds noise with no benefit.
- **CA1014 (CLSCompliant) disabled.** This is an application, not a redistributable
  library; CLS compliance is irrelevant.
- **CA1848 and CA2254 (LoggerMessage / structured logging) downgraded to
  `suggestion`.** The logging infrastructure does not exist yet; elevating these to
  errors before spec 003 would fail every placeholder that touches ILogger. Revisit
  when logging arrives.
- **TypeScript 5.4 over the template default (4.5).** Bumped to 5.4 (`~5.4.5`) with
  `strict: true` to match the constitution's analyzers-as-gates posture. The template
  targets 4.5 but 5.4 is stable and the stricter checker catches more at scaffold time.
- **typescript-eslint v7 over template v5.** v5 does not support TypeScript 5.x;
  v7 does, with full TS 5.4 compatibility. ESLint 8 and the `.eslintrc` (non-flat)
  config format are kept as-is since Electron Forge tooling has not migrated to v9.
- **Prettier added at scaffold time.** `prettier` (^3) with `singleQuote: true`,
  `trailingComma: "all"` and `format` / `format:check` scripts; `.prettierignore`
  covers `.webpack/` and `out/` build output. Enforces consistent style before any
  spec-010 code lands.
- **`endOfLine: auto` in Prettier config (T008).** The scaffold originally wrote
  `"endOfLine": "crlf"`. This passed on the Windows working tree but failed the
  Linux CI runner because `.gitattributes` normalizes files to LF on checkout, so
  Prettier saw LF-terminated files and reported a mismatch. Changed to `"auto"` so
  Prettier defers to the line ending git delivers on each platform.
- **`renderer.ts` renamed `renderer.tsx`.** The renderer entry uses JSX; TypeScript
  requires the `.tsx` extension. Forge `entryPoints` updated accordingly.
- **DevTools auto-open removed.** The template calls `mainWindow.webContents.openDevTools()`
  unconditionally; removed so the placeholder window opens clean. Dev tooling is a
  spec-010 decision.
- **`build` script aliases `electron-forge package`.** The CI table in this plan uses
  `npm run build`; the script maps to `electron-forge package` (produces a platform
  binary without a full installer, keeping the build fast).
- **`engines: { node: ">=20" }`.** Pins the minimum Node runtime to 20 per the
  pinned-toolchain NFR. Aligns with the `ubuntu-latest` / Node LTS runner in CI.
- **31 npm audit vulnerabilities in dev tooling.** These are known Electron Forge
  template dependencies (dev-only, no runtime path). Not addressed in T004; tracked
  as template noise. Spec 011 (packaging) owns dependency hardening.
- **gitleaks pinned to v8.30.1 with checksum verification (T008).** Installing via
  the GitHub Releases tarball (not `apt` or `brew`) pins an exact version and
  verifies the SHA-256 checksum before unpacking. This satisfies the supply-chain
  posture in constitution §4.1 and makes the runner behavior deterministic across
  runner image upgrades.
