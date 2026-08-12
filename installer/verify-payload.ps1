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

                # A missing file is a failure, not a skip. A deps.json only lands on disk for a
                # NON-single-file app -- native/ carries just LoreAgent.deps.json -- so every
                # managed assembly it lists must be sitting beside it. Treating absence as
                # "probably bundled" would blind this check to exactly the accident that happened
                # while building this gate: a single-file publish aimed at native/ swept away ~180
                # runtime files, agent included, and only the smoke test noticed. -SkipSmokeTest
                # would have left nothing watching at all.
                if (-not (Test-Path $file)) {
                    $mismatches += [pscustomobject]@{
                        App      = $deps.Name
                        Assembly = $name
                        Expected = $expected
                        Actual   = 'MISSING'
                        From     = $library.Name
                    }
                    continue
                }

                try {
                    $actual = [System.Reflection.AssemblyName]::GetAssemblyName($file).Version.ToString()
                }
                catch {
                    # deps.json promised a managed assembly with an assemblyVersion, so a file that
                    # will not yield one is corrupt or the wrong file -- report it rather than
                    # quietly passing. (Genuinely native and resource assets never reach here: they
                    # carry no assemblyVersion and were filtered out above.)
                    $mismatches += [pscustomobject]@{
                        App      = $deps.Name
                        Assembly = $name
                        Expected = $expected
                        Actual   = 'UNREADABLE'
                        From     = $library.Name
                    }
                    continue
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
# 2. Smoke launch: the shipped binaries must actually run.
# ---------------------------------------------------------------------------

# 2a. The CLI. It is the binary this gate's own change altered -- lore.exe went single-file --
# and without this it would be the one payload member never executed before shipping.
$cliExe = Join-Path $Path "lore.exe"
if (-not (Test-Path $cliExe)) { throw "lore.exe not found in $Path." }

Write-Host "==> Smoke-testing the built CLI"
# --help resolves no config, contacts no API, and writes nothing: it proves the single-file
# bundle unpacks and the managed entry point runs. The commands that would prove sibling
# resolution end-to-end (mcp install / skills install) all WRITE to real client config, so
# they are deliberately not run here.
$cliOutput = & $cliExe --help 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host ($cliOutput | Out-String)
    throw "The built lore.exe failed to run (exit $LASTEXITCODE)."
}
Write-Host "    lore.exe runs"

# 2b. The agent must serve the local API.
$agentExe = Join-Path $Path "LoreAgent.exe"
if (-not (Test-Path $agentExe)) { throw "LoreAgent.exe not found in $Path." }

Write-Host "==> Smoke-testing the built agent"

$stdout = Join-Path ([IO.Path]::GetTempPath()) "lore-smoke-out.txt"
$stderr = Join-Path ([IO.Path]::GetTempPath()) "lore-smoke-err.txt"
$statusUrl = "http://127.0.0.1:7842/system/status"

# The agent about to start is the real one, so without redirection it would run against the
# build machine's real state: migrate the developer's legacy data, read their config.json and
# the API key in it, and start capturing their screen (capture defaults on, 2s poll). Point it
# at a throwaway root instead -- LorePaths.DataDirectory funnels config, logs, the activity
# store and memoryd's data dir, and an overridden root also skips the legacy migration -- and
# switch capture off. config.json cannot exist in a fresh directory, so nothing outranks the
# capture setting here (config.json is layered over environment variables in Program.Main).
$scratchData = Join-Path ([IO.Path]::GetTempPath()) "lore-smoke-data-$PID"
if (Test-Path $scratchData) { Remove-Item $scratchData -Recurse -Force }
New-Item -ItemType Directory -Path $scratchData -Force | Out-Null

$env:LORE_DATA_DIR = $scratchData
$env:capture__Enabled = "false"

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

# Killing only the agent PID is not enough. The agent supervises memoryd and spawns
# native/memoryd/lore-memoryd.exe; Stop-Process -Force is a TerminateProcess, so the
# supervisor's own Kill(entireProcessTree) never runs and memoryd outlives the agent.
# build.ps1 then signs every *.exe under native/ recursively, and signtool cannot open a
# running exe for write -- an orphan here fails the signed release build, which is the one
# build nobody gets to retry quietly. Take the whole tree, then wait for the payload's own
# memoryd to actually be gone before returning.
function Stop-Agent {
    if ($agent -and -not $agent.HasExited) {
        # /T covers children the agent spawned; taskkill walks the tree, Stop-Process does not.
        & taskkill.exe /PID $agent.Id /T /F 2>&1 | Out-Null
    }

    $memorydExe = Join-Path $Path "memoryd\lore-memoryd.exe"
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        # Match on the payload's own path so a memoryd the developer was already running
        # elsewhere is left alone.
        $alive = Get-CimInstance Win32_Process -Filter "Name = 'lore-memoryd.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.ExecutablePath -and $_.ExecutablePath -eq $memorydExe }
        if (-not $alive) { return }
        foreach ($p in $alive) { Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Milliseconds 500
    }

    throw ("A memoryd from the payload is still running after the smoke test and would block " +
        "signing. Kill lore-memoryd.exe and re-run.")
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
    # Order matters: the tree has to be down before the scratch root can be removed, and both
    # have to happen even when the smoke test threw.
    Stop-Agent
    Remove-Item Env:\LORE_DATA_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\capture__Enabled -ErrorAction SilentlyContinue
    Remove-Item $scratchData -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "==> Payload verified: assemblies consistent, CLI runs, agent serves the local API."
