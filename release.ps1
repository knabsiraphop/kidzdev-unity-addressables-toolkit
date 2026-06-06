#!/usr/bin/env pwsh
# Release helper for com.kidzdev.addressables-toolkit
# Usage: .\release.ps1 <version>     e.g.  .\release.ps1 1.1.0
#
# Bumps the "version" field in package.json, then commits, tags, and pushes.

param(
    [Parameter(Position = 0)]
    [string]$Version
)

function Fail($message) {
    [Console]::Error.WriteLine("ERROR: $message")
    exit 1
}

# 1) Require a version argument.
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "Usage: .\release.ps1 <version>"
    Write-Host "Example: .\release.ps1 1.1.0"
    exit 1
}

# 4) Validate the version looks like X.Y.Z before doing anything.
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Fail "Version '$Version' is not valid. Expected X.Y.Z (e.g. 1.1.0)."
}

# Always operate from the repo root (this script's folder).
Set-Location -LiteralPath $PSScriptRoot

$packageJson = Join-Path $PSScriptRoot 'package.json'
if (-not (Test-Path -LiteralPath $packageJson)) {
    Fail "package.json not found at $packageJson"
}

# 2) Targeted replace of only the "version" value — preserve the rest of the file.
$content = Get-Content -LiteralPath $packageJson -Raw
$rx = [regex]'("version"\s*:\s*")[^"]*(")'
if (-not $rx.IsMatch($content)) {
    Fail 'Could not find a "version" field in package.json.'
}
$updated = $rx.Replace($content, "`${1}$Version`${2}", 1)  # replace first match only
[System.IO.File]::WriteAllText($packageJson, $updated)     # UTF-8 (no BOM), keeps line endings
Write-Host "Set version to $Version in package.json"

$tag = "v$Version"

# 3) Run git steps in order, stopping on any git error.
function Invoke-Git {
    param([string[]]$GitArgs, [switch]$AllowFailure)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        Fail "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE)."
    }
}

Invoke-Git @('add', '-A')
Invoke-Git @('commit', '-m', "Release $tag") -AllowFailure  # nothing to commit -> continue
Invoke-Git @('tag', $tag)
Invoke-Git @('push', 'origin', 'main')
Invoke-Git @('push', 'origin', $tag)

# 4) Confirm the tag landed on the remote.
Write-Host ""
Write-Host "Released version $Version (tag $tag)."
$remoteTag = & git ls-remote --tags origin $tag
if ([string]::IsNullOrWhiteSpace($remoteTag)) {
    Fail "Tag $tag was not found on origin after push."
}
Write-Host "Confirmed: $tag is on origin."
Write-Host $remoteTag
