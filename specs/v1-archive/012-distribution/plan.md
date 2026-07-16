# 012 — Package-Manager Distribution & Updates · Plan

> SDD artifact: **the how.** Decisions and approach behind
> [`specification.md`](specification.md). Bound by
> [`constitution.md`](../../constitution.md).

## Approach in one paragraph

A release is **a single tag push**. Tagging `vX.Y.Z` triggers a CI workflow that builds
the signed installer from pinned inputs (011's pipeline), publishes a GitHub Release
with the artifacts attached, and runs the winget-manifest generator to open a PR against
`microsoft/winget-pkgs`. The product version is single-sourced so the tag, the
installer, and `/system/status` always agree. Lore's own code is **untouched** — no
update check is added anywhere — so the egress posture is unchanged by construction.

## Key decisions

- **Tag-driven, not branch-driven.** Releases are cut by pushing an annotated `vX.Y.Z`
  tag. `installer.yml` already triggers on `push: tags: ["v*"]`; extend that path (or
  add a sibling `release.yml`) to publish the Release and artifacts. Pushes to `main`
  do **not** cut releases — they only run CI.

- **Single source of truth for version.** Define the version once and flow it outward:
  - Agent: set `<Version>` via a root `Directory.Build.props` (the agent currently has
    no `<Version>`, so it defaults to `1.0.0`). This makes `/system/status` `version`
    authoritative.
  - App: `app/package.json` `version` drives the Squirrel artifact name.
  - Keep them in lockstep — drive both from the **tag** at release time (CI writes the
    version into `Directory.Build.props`/`package.json` from `${GITHUB_REF_NAME}`), with
    a CI check that a tagged build's three version sources match. Pick the lowest-friction
    mechanism; the requirement is that they cannot silently drift.

- **winget identity.** Package id follows winget's `Publisher.Package` convention
  (e.g. `AmanJaiman.Lore` — confirm the owner's preferred publisher string). Moniker
  `lore`. This id is permanent once published, so it is a deliberate, owner-approved
  choice.

- **Squirrel ↔ winget mapping.** The Squirrel `LoreSetup.exe` is an `exe` installer that
  installs **per-user** silently by default. The manifest must therefore set
  `Scope: user`, the silent `InstallerSwitches` (Squirrel installs silently with no
  switch; document the verified behavior), and `AppsAndFeaturesEntries` (ProductCode +
  DisplayVersion) derived from what Squirrel writes to `HKCU\…\Uninstall`, so
  `winget upgrade` can match an installed version. These values are **discovered from a
  real install**, not guessed.

- **Manifest generation tooling.** Use Microsoft's `wingetcreate` (or `komac`) in the
  release workflow to regenerate the manifest from the published Release URL + SHA256
  and open the `winget-pkgs` PR via a maintainer PAT. The first version may be authored
  by hand and validated in Windows Sandbox to nail down the Squirrel-specific fields;
  automation takes over for subsequent versions.

- **Signing rides on 011.** No new signing work here; the release simply consumes the
  `LORE_SIGN_*` secrets 011 wired. If they're absent the Release still publishes an
  unsigned installer (documented interim), exactly as the local build does today.

- **No app code.** The renderer, agent, and `installer/` build logic are unchanged
  except for the version-source plumbing. This is deliberate: the smaller the code
  surface, the easier it is to assert criterion 5 (egress unchanged).

## Validation note (this spec only)

Per the owner's instruction for this spec, tasks are validated with **local checks +
CI on push to `main`** (`dotnet build -warnaserror`, `dotnet format
--verify-no-changes`, `dotnet test`, and the GitHub Actions gate), **not** the
`no-mistakes` gate — its console-popup interrupts the desktop, and CI already enforces
the same checks remotely. Merge verified branches to `main` with plain git
(`git merge --no-ff` + push). The winget submission + signing + public flip remain
human-gated (see the spec's "Human-gated" section).

## Dependencies & order

T001 (single-source version) gates everything — winget upgrade detection and the
artifact naming both depend on a coherent version. T002 (Release on tag) precedes T003
(manifest) because the manifest references the published Release URLs + hashes. T004
(automation) generalizes T003. T005 (docs) can land in parallel once the commands are
known.
