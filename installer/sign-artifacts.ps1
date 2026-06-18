<#
.SYNOPSIS
  Authenticode-sign the bundled native binaries — agent, CLI, memoryd (spec 011 T002).

.DESCRIPTION
  Code-signing is "build-ready" but opt-in (spec 011 T002 decision): the pipeline
  produces a working installer with or without a certificate, and signs only when one
  is configured via environment variables (so the cert lives in a CI secret, never in
  the repo). The installer itself is signed separately by electron-forge
  (app/forge.config.ts), which signs the app binaries + the Squirrel setup exe; this
  script covers the .NET/PyInstaller exes that forge treats as opaque resources.

  Configure via env (any of these unset => signing is skipped with a notice):
    LORE_SIGN_CERT_FILE   path to a .pfx, OR
    LORE_SIGN_CERT_SHA1   thumbprint of a cert already in the machine/user store
    LORE_SIGN_CERT_PASSWORD   .pfx password (when using LORE_SIGN_CERT_FILE)
    LORE_SIGN_TIMESTAMP_URL   RFC-3161 timestamp URL (default: DigiCert)

.PARAMETER Path
  Directory to sign all *.exe under (recursively).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Path
)

$ErrorActionPreference = "Stop"

$certFile = $env:LORE_SIGN_CERT_FILE
$certSha1 = $env:LORE_SIGN_CERT_SHA1
if (-not $certFile -and -not $certSha1) {
    Write-Host "==> Signing skipped (no LORE_SIGN_CERT_FILE / LORE_SIGN_CERT_SHA1 set); producing an unsigned build"
    return
}

$signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
if (-not $signtool) {
    # signtool ships with the Windows SDK; CI windows-latest has it but it isn't on PATH.
    $candidate = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $candidate) { throw "signtool.exe not found (install the Windows SDK)" }
    $signtool = $candidate.FullName
}
else {
    $signtool = $signtool.Source
}

$timestamp = if ($env:LORE_SIGN_TIMESTAMP_URL) { $env:LORE_SIGN_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }

$exes = Get-ChildItem $Path -Recurse -File -Include *.exe
Write-Host "==> Signing $($exes.Count) binaries under $Path"
foreach ($exe in $exes) {
    $args = @("sign", "/fd", "SHA256", "/tr", $timestamp, "/td", "SHA256")
    if ($certFile) {
        $args += @("/f", $certFile)
        if ($env:LORE_SIGN_CERT_PASSWORD) { $args += @("/p", $env:LORE_SIGN_CERT_PASSWORD) }
    }
    else {
        $args += @("/sha1", $certSha1)
    }
    $args += $exe.FullName
    & $signtool @args
    if ($LASTEXITCODE -ne 0) { throw "signtool failed for $($exe.FullName)" }
}
Write-Host "==> Native binaries signed"
