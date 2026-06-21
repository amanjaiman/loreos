<#
.SYNOPSIS
  Collect the three Squirrel release artifacts into a flat release/ dir for the
  GitHub Release upload (spec 012 T002).

.DESCRIPTION
  electron-forge / MakerSquirrel emits the installer under
  app/out/make/squirrel.windows/x64/:

    - LoreSetup.exe            (the setupExe from app/forge.config.ts)
    - Lore-X.Y.Z-full.nupkg    (the Squirrel package the installer unpacks)
    - RELEASES                 (Squirrel's manifest of available packages)

  This copies all three to <repo>/release/ and asserts the nupkg matches the
  expected version, so the Release upload globs are stable.

.PARAMETER Version
  The expected X.Y.Z; the staged nupkg must be Lore-<Version>-full.nupkg.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$makeDir = Join-Path $repoRoot "app/out/make/squirrel.windows/x64"
$releaseDir = Join-Path $repoRoot "release"

if (-not (Test-Path $makeDir)) {
    throw "Squirrel output not found at $makeDir - run installer/build.ps1 first."
}

if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $releaseDir | Out-Null

$expected = @{
    "LoreSetup.exe"                 = Join-Path $makeDir "LoreSetup.exe"
    "Lore-$Version-full.nupkg"      = Join-Path $makeDir "Lore-$Version-full.nupkg"
    "RELEASES"                      = Join-Path $makeDir "RELEASES"
}

foreach ($name in $expected.Keys) {
    $src = $expected[$name]
    if (-not (Test-Path $src)) {
        throw "Expected release artifact missing: $src"
    }
    Copy-Item $src (Join-Path $releaseDir $name) -Force
    Write-Host "==> staged $name"
}

Write-Host "==> Release artifacts staged under $releaseDir"
