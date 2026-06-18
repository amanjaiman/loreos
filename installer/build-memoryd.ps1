<#
.SYNOPSIS
  Freeze the memoryd sidecar into a single-folder Windows binary (spec 011 T001).

.DESCRIPTION
  Installs lore-memoryd and a pinned PyInstaller into the current Python
  environment, runs installer/memoryd.spec, then gates the result with
  `lore-memoryd.exe --selftest` (the clean-VM packaging check from the spec 002
  spike). Outputs installer/dist/lore-memoryd/ (a folder; the exe is inside it).

  Run from a Python 3.11 environment. CI (spec 011 T002) calls this from pinned
  inputs so the build is reproducible.

.PARAMETER OutDir
  Where to emit the frozen folder. Defaults to installer/dist.

.PARAMETER SkipSelfTest
  Skip the --selftest gate (e.g. cross-building where the host can't run the exe).
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [switch]$SkipSelfTest
)

$ErrorActionPreference = "Stop"
$installerDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerDir
$memorydDir = Join-Path $repoRoot "memoryd"
if (-not $OutDir) { $OutDir = Join-Path $installerDir "dist" }
$workDir = Join-Path $installerDir "build"

# PyInstaller must be pinned for reproducibility; this matches the spec 002 spike.
$pyInstallerVersion = "6.19.0"

Write-Host "==> Installing memoryd + PyInstaller $pyInstallerVersion"
python -m pip install --upgrade pip
if ($LASTEXITCODE -ne 0) { throw "pip upgrade failed" }
python -m pip install "$memorydDir"
if ($LASTEXITCODE -ne 0) { throw "memoryd install failed" }
python -m pip install "pyinstaller==$pyInstallerVersion"
if ($LASTEXITCODE -ne 0) { throw "pyinstaller install failed" }

Write-Host "==> Freezing memoryd -> $OutDir"
$spec = Join-Path $installerDir "memoryd.spec"
python -m PyInstaller --noconfirm --clean --distpath "$OutDir" --workpath "$workDir" "$spec"
if ($LASTEXITCODE -ne 0) { throw "PyInstaller build failed" }

$exe = Join-Path $OutDir "lore-memoryd\lore-memoryd.exe"
if (-not (Test-Path $exe)) { throw "expected frozen exe not found: $exe" }
Write-Host "==> Built $exe"

if ($SkipSelfTest) {
    Write-Host "==> Skipping --selftest (requested)"
    return
}

Write-Host "==> Running --selftest gate"
$report = & $exe --selftest
if ($LASTEXITCODE -ne 0) { throw "--selftest exited $LASTEXITCODE" }
Write-Host $report
$parsed = $report | ConvertFrom-Json
if ($parsed.frozen -ne "True") { throw "--selftest did not report a frozen build: $report" }
if ($parsed.mem0 -ne "2.0.5") { throw "--selftest reported unexpected mem0 version: $($parsed.mem0)" }
Write-Host "==> memoryd packaging OK (mem0 $($parsed.mem0), frozen $($parsed.frozen))"
