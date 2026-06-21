<#
.SYNOPSIS
  Single-source the product version into the .NET and app version files
  (spec 012 T001/T002).

.DESCRIPTION
  On a release tag (refs/tags/vX.Y.Z) this derives X.Y.Z from the tag and writes
  it into BOTH version sources before the build, so the agent assembly version,
  the Squirrel installer artifact name, and the tag all agree:

    - Directory.Build.props  <Version>X.Y.Z</Version>   (drives the agent assembly)
    - app/package.json       "version": "X.Y.Z"          (names LoreSetup's nupkg)

  On any other ref (e.g. workflow_dispatch with refs/heads/...) it does NOT modify
  the files — it reads the committed Directory.Build.props version and reports it,
  so a dry run validates the in-development version exactly as committed.

  The resolved version is written to $GITHUB_OUTPUT as `version` for later steps.

.PARAMETER Ref
  The git ref, e.g. the GitHub Actions ${{ github.ref }} ("refs/tags/v1.2.3" or
  "refs/heads/main").
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Ref
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$packagePath = Join-Path $repoRoot "app/package.json"

function Get-PropsVersion {
    [xml]$xml = Get-Content -Raw -Path $propsPath
    $node = $xml.SelectSingleNode("//Version")
    if ($null -eq $node) { throw "No <Version> in $propsPath" }
    return $node.InnerText.Trim()
}

if ($Ref -match '^refs/tags/v(?<v>\d+\.\d+\.\d+)$') {
    $version = $Matches['v']
    Write-Host "==> Release tag $Ref -> version $version; injecting into version sources"

    # Directory.Build.props: replace the single <Version>...</Version>.
    $props = Get-Content -Raw -Path $propsPath
    $props = [regex]::Replace($props, '<Version>[^<]*</Version>', "<Version>$version</Version>")
    Set-Content -Path $propsPath -Value $props -NoNewline -Encoding utf8

    # app/package.json: rewrite the "version" field, preserving the rest verbatim.
    $pkg = Get-Content -Raw -Path $packagePath
    $pkg = [regex]::Replace($pkg, '("version"\s*:\s*")[^"]*(")', "`${1}$version`${2}", 1)
    Set-Content -Path $packagePath -Value $pkg -NoNewline -Encoding utf8
}
else {
    $version = Get-PropsVersion
    Write-Host "==> Non-tag ref $Ref -> using committed version $version (no files modified)"
}

if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
Write-Host $version
