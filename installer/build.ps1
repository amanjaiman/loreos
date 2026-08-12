<#
.SYNOPSIS
  Assemble the Lore Windows installer: agent + memoryd + CLI + skills, bundled by
  electron-forge into one Squirrel installer (spec 011 T002).

.DESCRIPTION
  Orchestrates the full packaging pipeline from pinned inputs so the build is
  reproducible in CI:

    1. dotnet publish the agent  (self-contained win-x64) -> native/
    2. dotnet publish the CLI    (self-contained, single-file) -> native/  (sibling of the agent)
    3. stage skills/lore         -> native/skills/lore
    4. build the frozen memoryd  -> native/memoryd          (build-memoryd.ps1)
    5. verify the payload         (assembly versions + agent smoke launch; verify-payload.ps1)
    6. sign the native exes       (only if a signing cert is configured)
    7. electron-forge make        -> app/out/make/...        (bundles native/ + signs the installer)

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
#
# The frozen memoryd is the one input worth carrying across that clean. -SkipMemoryd exists
# precisely to avoid the multi-minute PyInstaller step, but it used to be dead: this wipe ran
# unconditionally, so the reuse check in step 4 could never find anything and every build paid
# the freeze no matter what was passed. Stash it first, restore it after — inside
# installer/build/, which is already gitignored and on the same volume as the repo (Move-Item
# will not move a directory across volumes, so %TEMP% is not a safe stash location).
$memorydDir = Join-Path $nativeDir "memoryd"
$memorydStash = $null
if ($SkipMemoryd -and (Test-Path (Join-Path $memorydDir "lore-memoryd.exe"))) {
    $stashRoot = Join-Path $installerDir "build"
    New-Item -ItemType Directory -Path $stashRoot -Force | Out-Null
    $memorydStash = Join-Path $stashRoot "memoryd-stash"
    if (Test-Path $memorydStash) { Remove-Item $memorydStash -Recurse -Force }
    Write-Host "==> Preserving existing native/memoryd across the clean"
    Move-Item $memorydDir $memorydStash
}

if (Test-Path $nativeDir) { Remove-Item $nativeDir -Recurse -Force }
New-Item -ItemType Directory -Path $nativeDir | Out-Null

if ($null -ne $memorydStash) { Move-Item $memorydStash $memorydDir }

# 1 + 2. Publish the agent and the CLI self-contained into the SAME directory, so
# lore.exe finds LoreAgent.exe as a sibling (cli McpInstallCommand.ResolveAgentPath).
#
# The CLI publishes SINGLE-FILE, and that is load-bearing — not a size optimization.
# Two self-contained apps sharing one output directory also share ~200 framework
# assemblies, and where their required versions differ the second publish overwrites
# the first. The agent needs System.Text.Json 10.0.6 (ModelContextProtocol 1.4.0
# requires >= 10.0.7); the CLI is plain net8.0 and carries the runtime pack's 8.0.x.
# Worse, those copies use PreserveNewest, so the winner is decided by the NuGet cache's
# file timestamps: on a developer box the long-extracted runtime pack loses and the
# build works, while on a clean CI runner everything is extracted fresh in one restore
# and it is a coin flip. v0.1.0 lost that flip and shipped an agent that died at
# startup with a FileNotFoundException for System.Text.Json 10.0.0.0, crash-looping
# behind the app's supervisor so every surface reported "Lore isn't running".
#
# Single-file bundles the CLI's managed assemblies inside lore.exe, so it contributes
# nothing to native/ but the exe itself and cannot collide with the agent. The sibling
# contracts are unaffected: AppContext.BaseDirectory is the executable's directory for
# single-file apps, so LoreAgent.exe and skills/lore still resolve. Step 5 asserts the
# collision has not returned by any other route.
#
# The CLI must publish into its OWN directory and have lore.exe copied across, never
# straight into native/. Single-file publish cleans the bundled files out of its output
# directory, and it does not distinguish its own from anyone else's: pointed at native/
# it deletes the ~180 files it bundled, which is the agent's entire runtime -- coreclr,
# hostpolicy, System.Private.CoreLib. The agent then dies with "hostpolicy.dll not
# found". The fix for the collision must not become the next way to ship a dead agent.
$cliStage = Join-Path $installerDir "build\cli-publish"
if (Test-Path $cliStage) { Remove-Item $cliStage -Recurse -Force }

Invoke-Checked "Publishing agent ($runtime, self-contained)" {
    dotnet publish (Join-Path $repoRoot "agent\LoreAgent.csproj") `
        -c $Configuration -r $runtime --self-contained true `
        -p:PublishSingleFile=false -o $nativeDir
}
Invoke-Checked "Publishing CLI ($runtime, self-contained, single-file)" {
    dotnet publish (Join-Path $repoRoot "cli\Lore.Cli.csproj") `
        -c $Configuration -r $runtime --self-contained true `
        -p:PublishSingleFile=true -o $cliStage
}
Write-Host "==> Staging lore.exe next to the agent"
Copy-Item (Join-Path $cliStage "lore.exe") $nativeDir -Force
# The CLI's symbols travel with it so a stack trace off a shipped lore.exe is readable.
$clipdb = Join-Path $cliStage "lore.pdb"
if (Test-Path $clipdb) { Copy-Item $clipdb $nativeDir -Force }

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
    if ($SkipMemoryd) {
        # Say so rather than silently spending the minutes the flag was meant to save.
        Write-Warning "-SkipMemoryd was passed but no existing native/memoryd was found; building it."
    }
    Invoke-Checked "Building frozen memoryd" {
        & (Join-Path $installerDir "build-memoryd.ps1") -OutDir $nativeDir
    }
    # build-memoryd emits native/lore-memoryd/; move it to native/memoryd/.
    $built = Join-Path $nativeDir "lore-memoryd"
    $target = Join-Path $nativeDir "memoryd"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Move-Item $built $target
}

# 5. Verify the assembled payload before spending the slow packaging steps on it: every
# assembly must match what its app's deps.json demands, and the agent must actually
# start and serve the local API. v0.1.0 packaged and shipped an agent that died in
# Program.Main, because nothing in this pipeline had ever run the binary it produced.
& (Join-Path $installerDir "verify-payload.ps1") -Path $nativeDir

# 6. Sign the native binaries (no-op unless a cert is configured).
& (Join-Path $installerDir "sign-artifacts.ps1") -Path $nativeDir

# 7. electron-forge make: bundles native/ (extraResource) and signs the installer.
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
