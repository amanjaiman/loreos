# winget manifest for Lore

The three-file winget manifest (spec [012-distribution](../../specs/012-distribution)
T003) that lets users `winget install Lore` and `winget upgrade Lore`. These files
are the **source** the release generator (T004) updates per version and the files
submitted to [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs).

The three YAMLs live under `manifest/` (so `winget validate --manifest` sees only
manifest files, not this README):

| File | Role |
|---|---|
| `manifest/AmanJaiman.Lore.yaml` | version manifest (root) |
| `manifest/AmanJaiman.Lore.installer.yaml` | installer manifest (URL, hash, scope, ARP/upgrade fields) |
| `manifest/AmanJaiman.Lore.locale.en-US.yaml` | defaultLocale manifest (display metadata) |

## Package identity (owner-gated, permanent)

The id `AmanJaiman.Lore` follows winget's `Publisher.Package` convention. **It is
permanent once published**, so the `Publisher` segment (`AmanJaiman`) and the
`Publisher` display name need owner confirmation before the first submission — every
`# TODO(owner)` line. Moniker is `lore`.

## What is NOT yet real (must be derived from a real install)

This environment can't run Windows Sandbox, the repo isn't public, and there's no
signing cert, so the install-derived fields are **placeholders** marked
`# TODO(sandbox)` / `# TODO(release)`. They must be filled and verified before the
first `winget-pkgs` PR:

- **`InstallerUrl` / `InstallerSha256`** (`# TODO(release)`) — filled automatically
  by the generator (T004) from the published GitHub Release asset. For a manual
  first submission: `(Get-FileHash LoreSetup.exe -Algorithm SHA256).Hash`.
- **`AppsAndFeaturesEntries.ProductCode` + `DisplayVersion`** (`# TODO(sandbox)`) —
  this is the load-bearing pair for `winget upgrade` detection. Squirrel registers a
  **per-user** uninstall entry under `HKCU`, and (unlike MSI) its "ProductCode" is
  typically the app name, not a GUID. You must read the real values:
- **`InstallerSwitches` silent behavior** (`# TODO(sandbox)`) — confirm Squirrel's
  setup exits 0 with no interactive prompt.

### Deriving the ARP fields (in Windows Sandbox)

1. Launch **Windows Sandbox** (clean Win10/11 VM; `WindowsSandbox.exe`).
2. Copy in `LoreSetup.exe` from the GitHub Release (or a local
   `installer/build.ps1` build) and run it. Let onboarding open, then close.
3. Read the uninstall entry Squirrel wrote:

   ```powershell
   Get-ChildItem HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall |
     ForEach-Object { Get-ItemProperty $_.PSPath } |
     Where-Object { $_.DisplayName -like '*Lore*' } |
     Select-Object DisplayName, DisplayVersion, PSChildName, InstallLocation
   ```

   - `PSChildName` (the registry key name) is what winget matches as `ProductCode`.
   - `DisplayVersion` goes into `AppsAndFeaturesEntries.DisplayVersion`.
   Copy both into `AmanJaiman.Lore.installer.yaml`, replacing the `# TODO(sandbox)`
   placeholders.

## Validate locally

With the [winget client](https://learn.microsoft.com/windows/package-manager/winget/)
installed (Sandbox or host):

```powershell
# Schema + cross-file consistency:
winget validate --manifest installer/winget/manifest

# Full install -> upgrade cycle (run inside a clean Sandbox):
winget install  --manifest installer/winget/manifest   # installs vX.Y.Z
# ...then bump the manifest to a newer version and:
winget upgrade  --manifest <next-version-manifest-dir> # must detect & upgrade the prior install
```

Until the `# TODO(sandbox)` / `# TODO(release)` fields are filled, `winget validate`
passes with a warning (empty silent switches) but `winget install` will fail on the
placeholder URL/hash — that's expected before the first real Release.

`winget upgrade` recognizing the prior install (via `AppsAndFeaturesEntries`) is the
acceptance test — if it reinstalls instead of upgrading, the ProductCode/DisplayVersion
don't match what Squirrel wrote; re-derive them above.

## Submission (human-gated)

The first `winget-pkgs` PR and Microsoft's review/merge are human-gated (see the
spec's "Human-gated" section). Subsequent versions are generated and submitted by the
release workflow (T004) — see [`../release-winget.ps1`](../release-winget.ps1).
