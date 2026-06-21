# 012 — Package-Manager Distribution & Updates · Tasks

> Each task is one PR (~1–4 h), dependency-ordered. `[deps: …]` lists prerequisites.
>
> **Validation for this spec:** local checks + CI on push, **not** the `no-mistakes`
> gate (owner's instruction — see [`plan.md`](plan.md#validation-note-this-spec-only)).
> Each task's "Done when" means: `dotnet build -warnaserror`, `dotnet format
> --verify-no-changes`, and `dotnet test` are green locally, and the GitHub Actions CI
> is green after merging the feature branch to `main` with plain git.

---

### T001 — Single-source product version  `[deps: none]`
Make one version authoritative and flow it to the agent assembly
(`/system/status` `version`) and `app/package.json` (installer artifact name). Today
they disagree (`package.json` = `0.1.0`, agent defaults to `1.0.0`). Add a root
`Directory.Build.props` with `<Version>` for the .NET projects; decide and document how
`package.json` stays in lockstep (driven from the tag at release time, or a synced
constant). Add a CI check that, on a `vX.Y.Z` tag build, the tag, `package.json`, and
the agent assembly version all match.
**Done when:** a build reports the same version from `/system/status`, the installer
artifact name, and the source — and a test/CI check fails if they drift.

### T002 — Publish a GitHub Release on a version tag  `[deps: T001, spec 011 T002]`
Extend `installer.yml` (or add `release.yml`) so a pushed `vX.Y.Z` tag builds the signed
installer from pinned inputs and publishes a GitHub **Release** with `LoreSetup.exe`,
`Lore-X.Y.Z-full.nupkg`, and `RELEASES` attached. Inject the tag version into the build
(T001). Unsigned still publishes if `LORE_SIGN_*` secrets are absent (documented).
**Done when:** pushing a test tag produces a Release with all three artifacts attached,
reproducibly. (Cutting a *real* public release tag stays human-gated.)

### T003 — Author + validate the winget manifest  `[deps: T002]`
Write the three-file winget manifest (version / installer / defaultLocale) for the
release. Derive the Squirrel-specific fields from a **real install** —
`AppsAndFeaturesEntries` (ProductCode, DisplayVersion), `Scope: user`, silent
`InstallerSwitches` — so `winget upgrade` detects an older install. Validate with
`winget validate` and an install→upgrade cycle in **Windows Sandbox**. Commit the
manifest under the repo (e.g. `installer/winget/`) as the source the generator updates.
**Done when:** `winget validate` passes and a Sandbox install→upgrade cycle works;
fields are documented. (The `winget-pkgs` PR submission + Microsoft review is
human-gated.)

### T004 — Automate manifest generation per release  `[deps: T003]`
Add a release step using `wingetcreate` (or `komac`) that, on a published Release,
regenerates the manifest from the Release URLs + SHA256 and opens/updates the
`winget-pkgs` PR via a maintainer PAT (stored as a CI secret). Idempotent: re-running a
release must not corrupt or duplicate the manifest.
**Done when:** a (dry-run/local) invocation produces a correct updated manifest for a
new version without manual edits; the PAT-driven submission path is wired and
documented (real submission human-gated).

### T005 — Docs: winget commands, privacy note, roadmap  `[deps: T002]`
- README: `winget install Lore` and `winget upgrade Lore`, alongside the existing
  Releases download.
- `docs/privacy.md`: an explicit line that updates flow through the package manager and
  Lore makes **no** update call — reinforcing the existing "No update check" guarantee,
  **not** adding an egress row. Confirm the egress table is unchanged.
- `docs/roadmap.md`: note Homebrew/macOS as the next distribution channel.
- `docs/launch-checklist.md`: point the "publish a GitHub Release" item at the automated
  tag flow.
**Done when:** the docs match the implemented commands and the privacy guarantee is
stated explicitly and verifiably (egress table diff is empty).

---

## Definition of done (spec)

All five acceptance criteria in [`specification.md`](specification.md#acceptance-criteria)
hold; in particular criterion 5 (**egress unchanged**) is verified by confirming no
outbound call was added to the app or agent and the `privacy.md` egress table is
untouched. Homebrew/macOS is explicitly deferred to the roadmap.
