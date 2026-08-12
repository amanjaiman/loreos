# installer/

Windows packaging for the agent, the bundled memoryd sidecar, and the app
(spec [011-packaging](../specs/011-packaging)).

## memoryd sidecar (T001)

`memoryd.spec` freezes the Python `lore_memoryd` service into a Windows binary so
end users never install Python. Build it from a Python 3.11 environment:

```powershell
./build-memoryd.ps1
```

This installs `lore-memoryd` + a pinned PyInstaller, runs the spec, and gates the
result with `lore-memoryd.exe --selftest` (the clean-VM check from the spec 002
spike: import the whole dependency tree, print bundled versions, exit 0). Output
lands in `installer/dist/lore-memoryd/` (`build/` and `dist/` are gitignored).

Notes:

- **`--onedir`, not `--onefile`** — `--onefile` re-extracts to a temp dir on every
  launch; the supervisor restarts the sidecar on crash, so that cost would recur.
  `--onedir` extracts once at install, so each launch is fast (~0.5 s cold) and
  stays inside the supervisor's health gate.
- **~175 MB** — `memoryd.spec` excludes the unused ML stack (torch/transformers)
  and mem0's unused optional integrations (playwright/googleapiclient/faiss/…),
  which mem0 imports lazily by provider name and Lore never configures. Without the
  excludes the payload is ~430 MB.

The agent's supervisor (spec 002) auto-discovers this exe in production: when a
build is laid out with `<agent dir>/memoryd/lore-memoryd.exe` it launches that;
otherwise it falls back to `python -m lore_memoryd` for from-source dev. The
installer (T002) lays the frozen folder into that conventional location.

## Installer assembly (T002)

`build.ps1` orchestrates the whole installer from pinned inputs:

```powershell
./build.ps1            # publish agent + CLI, stage skills + memoryd, electron-forge make
./build.ps1 -SkipMemoryd   # reuse an existing native/memoryd while iterating
```

Steps: `dotnet publish` the agent and CLI (self-contained `win-x64`) into one flat
`app/native/` tree, stage `skills/lore`, freeze memoryd into `native/memoryd`, sign
the native exes (if a cert is configured), then `electron-forge make` bundles
`native/` and produces the Squirrel installer under `app/out/make/`.

The flat layout matters: the CLI resolves `LoreAgent.exe` and `skills/lore` as
siblings, and the agent's supervisor resolves `memoryd/lore-memoryd.exe` relative to
itself (T001). All four ship in one tree that lands in `resources/native`.

**Runtime** (verified on a VM in T003): the app spawns and supervises `LoreAgent.exe`
for its lifetime ([app/src/agentProcess.ts](../app/src/agentProcess.ts)); the agent
in turn supervises memoryd. On install/update/uninstall the app keeps the bundled CLI
dir on the user PATH ([app/src/windowsIntegration.ts](../app/src/windowsIntegration.ts)).

### Code-signing (build-ready, opt-in)

Signing runs only when a certificate is configured, so the build works without one
(spec 011 T002 decision); the cert lives in a CI secret, never in the repo. Set:

| Variable | Meaning |
|---|---|
| `LORE_SIGN_CERT_FILE` | path to a `.pfx` (CI decodes `LORE_SIGN_CERT_BASE64` secret to this) |
| `LORE_SIGN_CERT_PASSWORD` | `.pfx` password |
| `LORE_SIGN_CERT_SHA1` | alternative: thumbprint of a cert already in the store |
| `LORE_SIGN_TIMESTAMP_URL` | RFC-3161 timestamp URL (default: DigiCert) |

`sign-artifacts.ps1` signs the .NET/PyInstaller exes; electron-forge signs the
Electron app binaries + the Squirrel setup exe (`app/forge.config.ts`).

### CI

[`.github/workflows/installer.yml`](../.github/workflows/installer.yml) builds the
installer on `windows-latest` on demand (`workflow_dispatch`) and on `v*` tags, and
uploads it as the `lore-installer` artifact. It's separate from the per-PR gates
(`ci.yml`) because it's heavy (self-contained publish + PyInstaller + forge make).

## Release on a version tag (T002, spec 012)

[`.github/workflows/release.yml`](../.github/workflows/release.yml) makes a release
**a single tag push**. Pushing an annotated `vX.Y.Z` tag:

1. **Injects the tag version** into both version sources
   ([`set-version.ps1`](set-version.ps1)): the root `Directory.Build.props`
   `<Version>` (drives the agent assembly / `GET /system/status`) and
   `app/package.json` `"version"` (names the Squirrel `.nupkg`). This is the
   single source of truth — the committed `0.1.0` is the in-development value; the
   tag is authoritative at release time.
2. Builds the signed installer with [`build.ps1`](build.ps1) (unsigned if the
   `LORE_SIGN_*` secrets are absent — documented interim).
3. **Asserts the tag, `package.json`, and the built agent assembly version all
   match** ([`verify-version.ps1`](verify-version.ps1)) — acceptance criterion 1;
   the release fails on any drift. On every PR the same `package.json ↔ agent`
   invariant is also enforced by `LoreAgent.Tests.VersionSyncTests`.
4. Stages the three artifacts ([`stage-release.ps1`](stage-release.ps1)) and
   **publishes a GitHub Release** with `LoreSetup.exe`, `Lore-X.Y.Z-full.nupkg`,
   and `RELEASES` attached — acceptance criterion 2.

`workflow_dispatch` runs a **dry run**: it builds and verifies against the committed
version but does not modify the version files or publish a Release. Pushes to `main`
never cut releases (only `ci.yml` runs there). Cutting a real public tag is
human-gated.

### Bumping the in-development version

When not tagging, bump `Directory.Build.props` `<Version>` and `app/package.json`
`"version"` **together** (the test fails the build otherwise).

## In-app auto-updates (Hazel)

Separately from winget, packaged **Windows** builds update themselves in the
background via Squirrel + a [Hazel](https://github.com/vercel/hazel) update server.

- **Client** ([`app/src/autoUpdate.ts`](../app/src/autoUpdate.ts)): the built-in
  Electron `autoUpdater` (Squirrel.Windows) points at
  `https://lore-hazel.vercel.app/update/win32/<current-version>`, checks on launch
  and every 10 minutes, and applies the newest `.nupkg` from the feed. When the
  window is visible it asks the user to restart; otherwise it installs silently so
  the next launch is already current. No-op in dev and on non-Windows.
- **Server**: the `lore-hazel` deployment on Vercel proxies this repo's **private**
  GitHub Releases. It's configured entirely through Vercel env vars — there is no
  config in this repo:

  | Var | Value |
  |---|---|
  | `ACCOUNT` | `amanjaiman` |
  | `REPO` | `loreos` |
  | `TOKEN` | a GitHub PAT with **read** access to the private `loreos` repo (required — the repo is private) |
  | `URL` | `https://lore-hazel.vercel.app` |

  Changing env vars requires a redeploy to take effect.
- **Publishing an update** is just cutting a higher-versioned tag (the release flow
  above): Hazel serves the new Release's `RELEASES` + `.nupkg`, and installed apps
  pick it up on their next poll.

> **Note — two channels.** This is a second update path alongside the winget
> distribution (spec 012, which deliberately shipped *no* in-app check). A Squirrel
> self-update moves the install out from under winget's version tracking, so a
> machine can report one version in `winget list` and another in the app. Keep
> Squirrel/Hazel as the primary channel and treat winget as discovery/first-install.
