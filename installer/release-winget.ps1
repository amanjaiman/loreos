<#
.SYNOPSIS
  Regenerate and submit the winget manifest for a published release (spec 012 T004).

.DESCRIPTION
  After release.yml publishes a GitHub Release, this updates the Lore winget
  manifest to the new version using Microsoft's `wingetcreate` and (optionally)
  opens/updates the PR against microsoft/winget-pkgs via a maintainer PAT.

  It is idempotent: `wingetcreate update` regenerates the manifest from the
  package id's current state + the new installer URL/hash, so re-running the same
  release version produces the same manifest and refreshes (does not duplicate)
  the PR.

  The InstallerUrl is the published Release asset; wingetcreate downloads it and
  computes the SHA256 itself, so the hash is never hand-entered.

.PARAMETER Version
  The released X.Y.Z (no leading 'v').

.PARAMETER InstallerUrl
  The public URL of the released LoreSetup.exe. Defaults to the GitHub Release
  asset URL for -Version.

.PARAMETER Submit
  Actually open/update the winget-pkgs PR (requires -Token). Omit for a dry run
  that only regenerates the manifest locally under ./winget-out.

.PARAMETER Token
  GitHub PAT with rights to fork + PR microsoft/winget-pkgs. In CI this is the
  WINGET_PAT secret; never hard-code it.

.NOTES
  Real submission is human-gated: the maintainer supplies WINGET_PAT and the first
  PR is reviewed by Microsoft. Without -Submit this is a safe local regeneration.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$InstallerUrl,
    [switch]$Submit,
    [string]$Token
)

$ErrorActionPreference = "Stop"
$packageId = "AmanJaiman.Lore" # TODO(owner): keep in lockstep with installer/winget/manifest.

if (-not $InstallerUrl) {
    $InstallerUrl = "https://github.com/amanjaiman/loreos/releases/download/v$Version/LoreSetup.exe"
}

# Ensure wingetcreate is available (winget-installed on a Windows runner, or via the
# bootstrap script). We don't pin here beyond what CI provides; document the version
# in the workflow for reproducibility.
$wc = Get-Command wingetcreate -ErrorAction SilentlyContinue
if (-not $wc) {
    throw "wingetcreate not found. Install it: winget install Microsoft.WingetCreate"
}

$outDir = Join-Path $PSScriptRoot "winget-out"
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $outDir | Out-Null

# `wingetcreate update` pulls the current manifest for $packageId from winget-pkgs,
# swaps in the new version + installer URL, recomputes the hash, and (with --submit)
# opens/updates the PR. For the FIRST ever version there is no upstream manifest yet;
# that one is the hand-authored installer/winget/manifest submitted manually (T003).
$wcArgs = @(
    "update", $packageId,
    "--version", $Version,
    "--urls", $InstallerUrl,
    "--out", $outDir
)

if ($Submit) {
    if (-not $Token) { throw "-Submit requires -Token (the winget-pkgs PAT)." }
    $wcArgs += @("--submit", "--token", $Token)
    Write-Host "==> wingetcreate update $packageId $Version (submitting PR to winget-pkgs)"
}
else {
    Write-Host "==> wingetcreate update $packageId $Version (dry run; regenerating manifest under $outDir)"
}

& wingetcreate @wcArgs
if ($LASTEXITCODE -ne 0) { throw "wingetcreate failed (exit $LASTEXITCODE)" }

Write-Host "==> winget manifest for $Version generated under $outDir"
if (-not $Submit) {
    Write-Host '    (dry run - no PR opened; pass -Submit -Token PAT to submit)'
}
