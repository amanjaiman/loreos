<#
.SYNOPSIS
  Assert the assembled native payload actually runs: no clobbered assemblies, and an
  agent that answers the local API.

.DESCRIPTION
  Two gates over app/native/, both added after v0.1.0 shipped an agent that could not
  start at all.

    1. Assembly integrity. Every published .NET app records, in its deps.json, the exact
       AssemblyVersion it expects for each assembly it loads. This walks each app's
       deps.json and compares that against the file actually on disk. Any drift is the
       FileNotFoundException the runtime will throw at startup, caught at build time.

       This is not theoretical. The agent and the CLI publish into one directory so
       lore.exe finds LoreAgent.exe as a sibling, which also makes them share ~200
       framework assemblies. Where their versions differ the second publish overwrites
       the first, and because those copies are PreserveNewest the winner comes down to
       NuGet cache timestamps -- stable on a dev box, a coin flip on a clean runner.
       v0.1.0 lost: the agent's System.Text.Json 10.0.6 was replaced by the runtime
       pack's 8.0.x and every launch died in Program.Main. build.ps1 now publishes the
       CLI single-file so it contributes no assemblies to collide with, and this check
       fails the build if that collision ever returns by another route.

    2. Smoke launch. Start LoreAgent.exe from the payload and poll GET /system/status
       until it answers. A payload that assembles, signs, and packages perfectly is
       still worthless if the agent exits on startup -- nothing in the pipeline had ever
       actually run the thing it shipped. Agent stdout/stderr is captured and echoed on
       failure so a crash arrives as a stack trace in the build log, not as a silent
       "Lore isn't running" on a user's machine.

.PARAMETER Path
  The assembled native payload directory. Defaults to app/native.

.PARAMETER TimeoutSeconds
  How long to wait for the agent to serve /system/status. Defaults to 60.

.PARAMETER SkipSmokeTest
  Run only the assembly-integrity check. For environments where binding the loopback
  port or spawning the agent is not possible.
#>
[CmdletBinding()]
param(
    [string]$Path,
    [int]$TimeoutSeconds = 60,
    [switch]$SkipSmokeTest
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Path) { $Path = Join-Path $repoRoot "app/native" }
if (-not (Test-Path $Path)) {
    throw "Native payload not found at $Path - run installer/build.ps1 first."
}
$Path = (Resolve-Path $Path).Path

# ---------------------------------------------------------------------------
# 1. Assembly integrity: deps.json expectations vs the files on disk.
# ---------------------------------------------------------------------------

Write-Host "==> Verifying payload assembly versions"

$depsFiles = Get-ChildItem $Path -Filter "*.deps.json"
if ($depsFiles.Count -eq 0) {
    throw "No .deps.json found in $Path - is this really an assembled payload?"
}

$mismatches = @()
foreach ($deps in $depsFiles) {
    $manifest = Get-Content $deps.FullName -Raw | ConvertFrom-Json
    foreach ($target in $manifest.targets.PSObject.Properties) {
        foreach ($library in $target.Value.PSObject.Properties) {
            $runtimeAssets = $library.Value.runtime
            if (-not $runtimeAssets) { continue }
            foreach ($asset in $runtimeAssets.PSObject.Properties) {
                # Resource satellites and native assets carry no assemblyVersion.
                $expected = $asset.Value.assemblyVersion
                if (-not $expected) { continue }

                $name = Split-Path $asset.Name -Leaf
                $file = Join-Path $Path $name
                if (-not (Test-Path $file)) {
                    # Single-file apps bundle their assemblies inside the exe, so a
                    # deps.json entry with no file beside it is expected, not missing.
                    continue
                }

                try {
                    $actual = [System.Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString()
                }
                catch {
                    continue  # not a managed assembly
                }

                if ($actual -ne $expected) {
                    $mismatches += [pscustomobject]@{
                        App      = $deps.Name
                        Assembly = $name
                        Expected = $expected
                        Actual   = $actual
                        From     = $library.Name
                    }
                }
            }
        }
    }
}

if ($mismatches.Count -gt 0) {
    Write-Host ""
    $mismatches | Format-Table -AutoSize | Out-String | Write-Host
    throw ("Payload assembly drift: $($mismatches.Count) assembly/assemblies on disk do not match " +
        "the version their app's deps.json demands. These will throw FileNotFoundException at startup.")
}

Write-Host "    $($depsFiles.Count) app(s) checked; all assembly versions match."

if ($SkipSmokeTest) {
    Write-Host "==> Skipping smoke test (-SkipSmokeTest)"
    return
}

# ---------------------------------------------------------------------------
# 2. Smoke launch: the agent must actually serve the local API.
# ---------------------------------------------------------------------------

$agentExe = Join-Path $Path "LoreAgent.exe"
if (-not (Test-Path $agentExe)) { throw "LoreAgent.exe not found in $Path." }

Write-Host "==> Smoke-testing the built agent"

$stdout = Join-Path ([IO.Path]::GetTempPath()) "lore-smoke-out.txt"
$stderr = Join-Path ([IO.Path]::GetTempPath()) "lore-smoke-err.txt"
$statusUrl = "http://127.0.0.1:7842/system/status"

# Fail loudly rather than "passing" against an agent someone else already had running.
try {
    $inUse = Get-NetTCPConnection -LocalPort 7842 -State Listen -ErrorAction SilentlyContinue
}
catch { $inUse = $null }
if ($inUse) {
    throw "Port 7842 is already in use - stop the running agent so this tests the built one."
}

$agent = Start-Process -FilePath $agentExe -WorkingDirectory $Path -NoNewWindow -PassThru `
    -RedirectStandardOutput $stdout -RedirectStandardError $stderr

# Touching Handle now makes .NET cache it, which is what keeps ExitCode readable after
# the process dies. Skip this and a crashed agent is reported as "exited (code )" -
# dropping the one number that says whether it faulted or exited cleanly.
$null = $agent.Handle

function Stop-Agent {
    if ($agent -and -not $agent.HasExited) { Stop-Process -Id $agent.Id -Force -ErrorAction SilentlyContinue }
}

function Show-AgentOutput {
    foreach ($pair in @(@("stdout", $stdout), @("stderr", $stderr))) {
        # The agent writes UTF-8; without this 5.1 decodes as ANSI and mangles the log.
        $text = Get-Content $pair[1] -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        if ($text) { Write-Host "--- agent $($pair[0]) ---"; Write-Host $text }
    }
}

# HasExited alone leaves ExitCode unpopulated on the object Start-Process handed back,
# which reports a crashed agent as "exited (code )". WaitForExit on an already-exited
# process returns at once and fills it in.
function Get-AgentExitCode {
    try { $agent.WaitForExit(); return $agent.ExitCode } catch { return "unknown" }
}

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $ok = $false
    while ((Get-Date) -lt $deadline) {
        # An agent that died is never coming back; surface its crash immediately
        # instead of burning the full timeout.
        if ($agent.HasExited) {
            Show-AgentOutput
            throw "The agent exited (code $(Get-AgentExitCode)) before serving $statusUrl."
        }

        try {
            $response = Invoke-WebRequest -Uri $statusUrl -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { $ok = $true; break }
        }
        catch {
            Start-Sleep -Milliseconds 500  # not up yet
        }
    }

    if (-not $ok) {
        Show-AgentOutput
        throw "The agent did not serve $statusUrl within ${TimeoutSeconds}s."
    }

    Write-Host "    agent served $statusUrl"
    Write-Host "    $($response.Content)"
}
finally {
    Stop-Agent
}

Write-Host "==> Payload verified: assemblies consistent, agent starts and serves the local API."
