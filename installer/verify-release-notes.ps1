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

  Requires full history and tags (actions/checkout with fetch-depth: 0).

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

Write-Host "==> Release notes check"
Write-Host "    version            : $Version"

if (-not (Test-Path $notesPath)) {
    throw "$notesRelative not found - curate it with .agents/skills/write-changelog before tagging."
}

$notes = Get-Content -Raw -Path $notesPath
if ([string]::IsNullOrWhiteSpace($notes)) {
    throw "$notesRelative is empty - curate it with .agents/skills/write-changelog before tagging."
}

# Newest release tag that isn't the one being released. `-v:refname` sorts by version,
# so this is the release these notes must have moved on from.
$currentTag = "v$Version"
$previousTag = git -C $repoRoot tag --list "v*" --sort=-v:refname |
    Where-Object { $_ -ne $currentTag } |
    Select-Object -First 1

if (-not $previousTag) {
    Write-Host "    previous tag       : (none - first release)"
    Write-Host "==> $notesRelative is present; nothing to compare against"
    exit 0
}

Write-Host "    previous tag       : $previousTag"

# A tag that predates the file itself has nothing to be stale against.
$tracked = git -C $repoRoot ls-tree --name-only "$previousTag" -- "$notesRelative"
if (-not $tracked) {
    Write-Host "==> $notesRelative did not exist at $previousTag - notes are new"
    exit 0
}

# Compare on normalized line endings so a checkout's autocrlf can't read as a change.
$previousNotes = (git -C $repoRoot show "${previousTag}:${notesRelative}") -join "`n"
$current = ($notes -replace "`r`n", "`n").Trim()
$previous = ($previousNotes -replace "`r`n", "`n").Trim()
if ($current -eq $previous) {
    throw "$notesRelative is unchanged since $previousTag - it still describes that release. Curate it for $currentTag with .agents/skills/write-changelog before tagging."
}

Write-Host "==> $notesRelative was updated since $previousTag"
