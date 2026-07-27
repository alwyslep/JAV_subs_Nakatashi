<#
.SYNOPSIS
    Sync this fork with SubtitleEdit/subtitleedit (upstream).

.DESCRIPTION
    Fetches upstream, then rebases (default) or merges the local branch onto
    upstream/main and optionally pushes to origin. Refuses to run with a dirty
    working tree so an interrupted rebase can never eat uncommitted work.

.EXAMPLE
    ./tools/sync-upstream.ps1                  # rebase current branch onto upstream/main
    ./tools/sync-upstream.ps1 -Strategy merge  # merge instead (keeps a merge commit)
    ./tools/sync-upstream.ps1 -Push            # rebase, then push to origin
#>
[CmdletBinding()]
param(
    [ValidateSet('rebase', 'merge')]
    [string]$Strategy = 'rebase',

    [string]$Branch = 'main',

    [switch]$Push
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$RepoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Git {
    param([string[]]$Arguments)
    Write-Host "    git $($Arguments -join ' ')" -ForegroundColor DarkGray
    & git -C $RepoRoot @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

# --- preconditions -----------------------------------------------------------

$remotes = & git -C $RepoRoot remote
if ($remotes -notcontains 'upstream') {
    Write-Host '==> Adding missing upstream remote' -ForegroundColor Cyan
    Invoke-Git @('remote', 'add', 'upstream', 'https://github.com/SubtitleEdit/subtitleedit.git')
}

$dirty = & git -C $RepoRoot status --porcelain
if ($dirty) {
    Write-Host 'Working tree is not clean. Commit or stash first:' -ForegroundColor Red
    $dirty | ForEach-Object { Write-Host "    $_" }
    exit 1
}

$current = (& git -C $RepoRoot rev-parse --abbrev-ref HEAD).Trim()

# --- sync --------------------------------------------------------------------

Write-Host '==> Fetching upstream' -ForegroundColor Cyan
Invoke-Git @('fetch', 'upstream', '--prune')

$behind = (& git -C $RepoRoot rev-list --count "HEAD..upstream/$Branch").Trim()
$ahead = (& git -C $RepoRoot rev-list --count "upstream/$Branch..HEAD").Trim()
Write-Host "    $current is $ahead ahead / $behind behind upstream/$Branch"

if ($behind -eq '0') {
    Write-Host 'Already up to date with upstream.' -ForegroundColor Green
}
else {
    Write-Host "==> $Strategy onto upstream/$Branch" -ForegroundColor Cyan
    Invoke-Git @($Strategy, "upstream/$Branch")
}

if ($Push) {
    Write-Host '==> Pushing to origin' -ForegroundColor Cyan
    # A rebase rewrites local history, so the push needs --force-with-lease.
    # --force-with-lease still refuses if origin moved behind our back.
    if ($Strategy -eq 'rebase') {
        Invoke-Git @('push', '--force-with-lease', 'origin', $current)
    }
    else {
        Invoke-Git @('push', 'origin', $current)
    }
}

Write-Host ''
Write-Host 'OK: upstream sync complete' -ForegroundColor Green
