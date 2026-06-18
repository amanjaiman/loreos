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
