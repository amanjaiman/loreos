<#
.SYNOPSIS
  Assert the product version is single-sourced across the tag, app/package.json,
  and the built agent assembly (spec 012 acceptance criterion 1).

.DESCRIPTION
  After a release build, three things must report the same X.Y.Z:

    - the release tag           (passed in as -Expected)
    - app/package.json          ("version")
    - the built agent assembly  (LoreAgent.dll ProductVersion - the same
                                 InformationalVersion GET /system/status reports)

  Any mismatch throws, failing the release. Build metadata after '+' in the
  assembly version is trimmed, mirroring SystemEndpoints.AppVersion.

.PARAMETER Expected
  The expected X.Y.Z (the release tag with its leading 'v' stripped). On a dry
  run this is the committed version from Directory.Build.props.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Expected
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# app/package.json version.
$pkg = Get-Content -Raw -Path (Join-Path $repoRoot "app/package.json") | ConvertFrom-Json
$pkgVersion = $pkg.version

# Built agent assembly version. installer/build.ps1 publishes the self-contained
# agent into app/native/; LoreAgent.dll carries the InformationalVersion.
$agentDll = Join-Path $repoRoot "app/native/LoreAgent.dll"
if (-not (Test-Path $agentDll)) {
    throw "Built agent not found at $agentDll - run installer/build.ps1 first."
}
$agentVersion = (Get-Item $agentDll).VersionInfo.ProductVersion
# Trim build metadata after '+' (mirrors SystemEndpoints.AppVersion).
$plus = $agentVersion.IndexOf('+')
if ($plus -ge 0) { $agentVersion = $agentVersion.Substring(0, $plus) }

Write-Host "==> Version check"
Write-Host "    expected (tag)     : $Expected"
Write-Host "    app/package.json   : $pkgVersion"
Write-Host "    agent assembly     : $agentVersion"

$mismatch = @()
if ($pkgVersion -ne $Expected) { $mismatch += "package.json ($pkgVersion) != tag ($Expected)" }
if ($agentVersion -ne $Expected) { $mismatch += "agent assembly ($agentVersion) != tag ($Expected)" }

if ($mismatch.Count -gt 0) {
    throw "Version drift: $($mismatch -join '; ')"
}

Write-Host "==> All three version sources agree on $Expected"
