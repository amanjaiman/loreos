<#
.SYNOPSIS
  Assert release-notes.md was curated for the version being released.

.DESCRIPTION
  release.yml publishes the repository-root release-notes.md verbatim as the GitHub
  Release body, and Hazel serves that body to every installed app as the in-app
  changelog. Unlike `generate_release_notes`, a committed file is not
  correct-by-construction: a tag pushed without refreshing it would silently ship the
  previous release's notes to every user.

  This is the deterministic guard for that, in the spirit of verify-version.ps1:

    - release-notes.md must exist and carry actual content, and
    - it must differ from its content at the previous release tag.

  The first release (no earlier tag) and the tag that first introduces the file both
  pass. Anything else throws, failing the release before the build runs.

  Requires full history and tags (actions/checkout with fetch-depth: 0). The guard fails
  closed: a shallow checkout, or any git command that exits non-zero, throws rather than
  reading as "first release" / "notes are new" and waving a stale release through.

.PARAMETER Version
  The X.Y.Z being released (the tag with its leading 'v' stripped). Its own tag is
  excluded when picking the previous release tag, so this works both on a tag push and
  on a workflow_dispatch dry run of an already-released version.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$notesRelative = "release-notes.md"
$notesPath = Join-Path $repoRoot $notesRelative

# $ErrorActionPreference does not apply to a native command's exit code, and every git
# call here is one whose empty output would otherwise be read as "nothing to compare
# against" - i.e. a failure would pass the release. Route them all through this so a
# broken git is a failed release, not a silent one.
function Invoke-Git {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$What
    )
    $output = & git -C $repoRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$What failed (git exited $LASTEXITCODE). This check needs the release history: check out with fetch-depth: 0."
    }
    return $output
}

Write-Host "==> Release notes check"
Write-Host "    version            : $Version"

if (-not (Test-Path $notesPath)) {
    throw "$notesRelative not found - curate it with .agents/skills/write-changelog before tagging."
}

$notes = Get-Content -Raw -Path $notesPath
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw "$notesRelative is empty - curate it with .agents/skills/write-changelog before tagging."
}

# A shallow clone carries the pushed tag but not the ones before it, so "no previous tag"
# would be indistinguishable from a first release. Refuse to certify anything from one.
$shallow = Invoke-Git -Arguments @("rev-parse", "--is-shallow-repository") -What "Checking repository depth"
if ("$shallow".Trim() -eq "true") {
    throw "Shallow checkout - the release history needed to detect stale notes is missing. Check out with fetch-depth: 0."
}

# Newest release tag that isn't the one being released. `-v:refname` sorts by version,
# so this is the release these notes must have moved on from.
$currentTag = "v$Version"
$tags = Invoke-Git -Arguments @("tag", "--list", "v*", "--sort=-v:refname") -What "Listing release tags"
$previousTag = $tags |
    Where-Object { $_ -ne $currentTag } |
    Select-Object -First 1

if (-not $previousTag) {
    Write-Host "    previous tag       : (none - first release)"
    Write-Host "==> $notesRelative is present; nothing to compare against"
    exit 0
}

Write-Host "    previous tag       : $previousTag"

# A tag that predates the file itself has nothing to be stale against. `ls-tree` exits 0
# with no output for an untracked path, and non-zero for a tag it can't resolve — which
# Invoke-Git turns into a failure rather than a free pass.
$tracked = Invoke-Git -Arguments @("ls-tree", "--name-only", "$previousTag", "--", "$notesRelative") -What "Reading $notesRelative at $previousTag"
if (-not $tracked) {
    Write-Host "==> $notesRelative did not exist at $previousTag - notes are new"
    exit 0
}

# Compare on normalized line endings so a checkout's autocrlf can't read as a change.
$previousNotes = (Invoke-Git -Arguments @("show", "${previousTag}:${notesRelative}") -What "Reading $notesRelative at $previousTag") -join "`n"
$current = ($notes -replace "`r`n", "`n").Trim()
$previous = ($previousNotes -replace "`r`n", "`n").Trim()
if ($current -eq $previous) {
    throw "$notesRelative is unchanged since $previousTag - it still describes that release. Curate it for $currentTag with .agents/skills/write-changelog before tagging."
}

Write-Host "==> $notesRelative was updated since $previousTag"
