<#
.SYNOPSIS
  Assemble the Lore Windows installer: agent + memoryd + CLI + skills, bundled by
  electron-forge into one Squirrel installer (spec 011 T002).

.DESCRIPTION
  Orchestrates the full packaging pipeline from pinned inputs so the build is
  reproducible in CI:

    1. dotnet publish the agent  (self-contained win-x64) -> native/
    2. dotnet publish the CLI    (self-contained win-x64) -> native/  (sibling of the agent)
    3. stage skills/lore         -> native/skills/lore
    4. build the frozen memoryd  -> native/memoryd          (build-memoryd.ps1)
    5. sign the native exes       (only if a signing cert is configured)
    6. electron-forge make        -> app/out/make/...        (bundles native/ + signs the installer)

  The CLI resolves the agent and skills as siblings (AppContext.BaseDirectory),
  the agent's supervisor resolves memoryd at <dir>/memoryd/lore-memoryd.exe
  (spec 011 T001), so all four ship in one flat tree that electron-forge copies
  into the app's resources.

.PARAMETER Configuration
  dotnet build configuration. Defaults to Release.

.PARAMETER SkipMemoryd
  Reuse an existing native/memoryd (skip the slow PyInstaller step) for iteration.

.NOTES
  Signing is opt-in and "build-ready": set the LORE_WINDOWS_SIGN_* environment
  variables (see Sign-NativeArtifacts and app/forge.config.ts). With them unset the
  build still produces a working—unsigned—installer, so the pipeline runs without a
  certificate in hand (spec 011 T002 decision).
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$SkipMemoryd
)

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerDir
$appDir = Join-Path $repoRoot "app"
$nativeDir = Join-Path $appDir "native"
$runtime = "win-x64"

function Invoke-Checked {
    param([string]$What, [scriptblock]$Action)
    Write-Host "==> $What"
    & $Action
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit $LASTEXITCODE)" }
}

# A clean native/ each run so a removed input never lingers in the bundle.
if (Test-Path $nativeDir) { Remove-Item $nativeDir -Recurse -Force }
New-Item -ItemType Directory -Path $nativeDir | Out-Null

# 1 + 2. Publish the agent and the CLI self-contained into the SAME directory, so
# lore.exe finds LoreAgent.exe as a sibling (cli McpInstallCommand.ResolveAgentPath).
Invoke-Checked "Publishing agent ($runtime, self-contained)" {
    dotnet publish (Join-Path $repoRoot "agent\LoreAgent.csproj") `
        -c $Configuration -r $runtime --self-contained true `
        -p:PublishSingleFile=false -o $nativeDir
}
Invoke-Checked "Publishing CLI ($runtime, self-contained)" {
    dotnet publish (Join-Path $repoRoot "cli\Lore.Cli.csproj") `
        -c $Configuration -r $runtime --self-contained true `
        -p:PublishSingleFile=false -o $nativeDir
}

# 3. Stage the agent skill next to the CLI (cli SkillsInstallCommand resolves
# skills/lore as a sibling of lore.exe).
Write-Host "==> Staging skills/lore"
$skillsDst = Join-Path $nativeDir "skills\lore"
New-Item -ItemType Directory -Path $skillsDst -Force | Out-Null
Copy-Item (Join-Path $repoRoot "skills\lore\*") $skillsDst -Recurse -Force

# 4. Freeze memoryd into native/memoryd (the supervisor's conventional location).
if ($SkipMemoryd -and (Test-Path (Join-Path $nativeDir "memoryd\lore-memoryd.exe"))) {
    Write-Host "==> Skipping memoryd build (reusing existing native/memoryd)"
}
else {
    Invoke-Checked "Building frozen memoryd" {
        & (Join-Path $installerDir "build-memoryd.ps1") -OutDir $nativeDir
    }
    # build-memoryd emits native/lore-memoryd/; move it to native/memoryd/.
    $built = Join-Path $nativeDir "lore-memoryd"
    $target = Join-Path $nativeDir "memoryd"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Move-Item $built $target
}

# 5. Sign the native binaries (no-op unless a cert is configured).
& (Join-Path $installerDir "sign-artifacts.ps1") -Path $nativeDir

# 6. electron-forge make: bundles native/ (extraResource) and signs the installer.
Invoke-Checked "Installing app dependencies (npm ci)" {
    Push-Location $appDir
    try { npm ci } finally { Pop-Location }
}
Invoke-Checked "electron-forge make" {
    Push-Location $appDir
    try { npm run make } finally { Pop-Location }
}

$makeOut = Join-Path $appDir "out\make"
Write-Host "==> Installer build complete. Artifacts under: $makeOut"
Get-ChildItem $makeOut -Recurse -File -Include *.exe, *.nupkg, RELEASES |
    ForEach-Object { Write-Host "    $($_.FullName)" }
