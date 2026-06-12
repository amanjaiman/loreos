# 001 — Foundation · Specification

> SDD artifact: **what & why.** Architecture and file-level detail live in
> [`plan.md`](plan.md); the work breakdown lives in [`tasks.md`](tasks.md). All
> three inherit [`constitution.md`](../../constitution.md).

## Overview

Foundation bootstraps the `loreos` monorepo so that every later spec has a clean,
gated place to build. It establishes the repository skeleton, the open-source
licensing and front-door docs, the continuous-integration pipeline (build, lint,
test, secret-scan), and the `no-mistakes` delivery gate. It ships **no product
behavior** — its deliverable is a repository a stranger can clone, build the empty
component skeletons, and open a PR against, with CI enforcing the constitution's
rules from the very first commit.

This is the only spec with no upstream dependency. Specs 002–011 depend on it.

## User stories

- **As a contributor**, I can clone `loreos`, follow the README, and build every
  component skeleton with documented commands so I can start on any spec.
- **As a maintainer**, I want every PR automatically built, linted, tested, and
  secret-scanned so the constitution's standards are enforced by machines, not
  vigilance.
- **As a security-conscious user**, I can open the repo and immediately see the
  license, the privacy stance, and that no credentials are present, so I can trust
  the project before running it.
- **As an agent assigned a later spec**, I find a working solution structure, CI I
  must keep green, and a `no-mistakes`-initialized repo, so my first task isn't
  fighting scaffolding.

## Scope

### In scope

- Monorepo directory skeleton for all components (`agent/`, `cli/`, `memoryd/`,
  `app/`, `skills/`, `docs/`, `installer/`, `specs/`).
- Minimal, **buildable** skeletons: a compiling .NET solution with empty `agent`,
  `cli`, and `agent.tests` projects; an importable `memoryd` Python package with a
  `/health` route; an Electron/React app shell that boots to a placeholder window.
- Open-source files: `LICENSE` (Apache 2.0), `README.md`, `CONTRIBUTING.md`,
  `SECURITY.md`, `CODE_OF_CONDUCT.md`, `.gitignore`, `.editorconfig`.
- `docs/` seeds: `architecture.md` (from the project plan) and a `privacy.md`
  stub with the network-egress section scaffolded.
- CI pipeline (GitHub Actions): C# build + format + analyzers + test, Python
  ruff + mypy + pytest, app eslint + prettier + tsc + build, and `gitleaks` secret
  scan — all required on PRs to the default branch.
- Tooling configuration: `.editorconfig`, analyzer rulesets, `ruff`/`mypy` config
  in `pyproject.toml`, `eslint`/`prettier` config, `gitleaks` config.
- `no-mistakes init` run and committed; repo default branch protected.

### Out of scope

- Any capture, memory, inference, MCP, CLI, or app **behavior** (later specs).
- The mem0 dependency itself and its configuration (spec 002).
- Provider/credential handling (spec 004).
- Packaging/installer logic beyond the empty `installer/` directory (spec 011).
- Hosting/publishing the repo publicly (a launch-readiness activity, spec 011).

## Acceptance criteria

1. `dotnet build` at the repo root compiles the solution (agent, cli, agent.tests)
   with **zero warnings** under analyzers-as-errors.
2. `dotnet test` runs and passes a placeholder test in `agent.tests`.
3. `pip install -e memoryd && pytest memoryd` passes; `GET /health` on the memoryd
   app returns `200` with `{"status":"ok"}`.
4. `npm --prefix app install && npm --prefix app run build` succeeds; `npm --prefix
   app start` opens a placeholder window.
5. The CI workflow runs all four job groups (C#, Python, app, gitleaks) on a PR and
   is **required** to pass before merge; a deliberately introduced lint error and a
   planted fake secret each fail CI.
6. `LICENSE` is Apache 2.0; `README.md` contains the project one-liner and a
   "build every component" quickstart that matches reality.
7. `docs/privacy.md` exists with a "Network egress" section listing zero
   non-user-configured calls; `docs/architecture.md` describes the
   one-behavior-layer / thin-shim architecture.
8. `gitleaks` reports clean on the whole tree and history.
9. `no-mistakes` is initialized; a trivial change can be taken through the gate to a
   `checks-passed` outcome on a feature branch.

## Non-functional requirements

- **Reproducible:** clean checkout → green build on a fresh Windows 10/11 machine
  with documented prerequisites (.NET 8 SDK, Python 3.11+, Node LTS) only.
- **Fast CI:** the PR pipeline completes in under ~10 minutes on hosted runners.
- **Pinned toolchain:** SDK/runtime versions pinned (`global.json`, `.python-version`,
  app `engines`) so builds are deterministic.

## Risks

- *Analyzers-as-errors on empty projects can surface noise.* Mitigation: agree the
  ruleset here, once, so later specs inherit a known-good baseline.
- *Windows-only agent build complicates cross-OS CI.* Mitigation: C# jobs run on
  `windows-latest`; Python/app/gitleaks jobs run on `ubuntu-latest`.
