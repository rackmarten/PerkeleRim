<#
.SYNOPSIS
    Builds a dev build of main and puts it on the rolling `dev` pre-release on GitHub, so testers get
    what's merged between releases (docs/DEV_SETUP.md, "Dev builds"). The checkout must be a clean main
    that is origin/main. It builds both sides and the ESP (scripts/build.ps1 -Version, build-esp.ps1),
    packages tools/out/release/PerkeleRim-dev-<yyyymmdd>-<commit>.zip (scripts/package.ps1) with the MSC
    mod's version <next>-dev.<yyyymmdd>+<commit> and a "DEV BUILD.txt", and writes SHA256SUMS.txt and
    the notes (scripts/release-notes.ps1 -DevCommit). Then it moves the `dev` tag to the commit (or
    creates the pre-release, never marked latest), uploads the zip and SHA256SUMS.txt and deletes the
    older dev zip. Nothing deploys into or starts the games; build-esp.ps1 reads your Skyrim.esm.
    Only the maintainer runs it, or an agent the maintainer tells to.
.PARAMETER DryRun
    Builds and packages but uploads nothing; a checkout that isn't a clean origin/main only warns.
.EXAMPLE
    scripts/publish-dev.ps1 -DryRun
    scripts/publish-dev.ps1
#>
param([Alias('WhatIf')] [switch] $DryRun)

. "$PSScriptRoot/common.ps1"
. "$PSScriptRoot/dev-build.ps1"

function Invoke-Checked {
    param([string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

function Invoke-Git { git -C $RepoRoot @args }

# A dev build is main as origin has it, so the commit in the notes and in the MSC mod is one anyone can look up.
Invoke-Checked git @('-C', $RepoRoot, 'fetch', '--quiet', 'origin', 'main')
$commit = Invoke-Git rev-parse HEAD
$problems = Get-CheckoutProblems (Invoke-Git branch --show-current) @(Invoke-Git status --porcelain) $commit (Invoke-Git rev-parse --verify --quiet origin/main)
if ($problems) {
    $message = "Dev builds come from a clean main that matches origin/main:`n  $($problems -join "`n  ")"
    if (-not $DryRun) { throw $message }
    Write-Host "$message`nDry run, so building anyway." -ForegroundColor Yellow
}

$repoVersion = (Select-String -Path (Join-Path $RepoRoot 'skse/CMakeLists.txt') -Pattern '^\s*VERSION\s+(\S+)').Matches[0].Groups[1].Value
# Release tags from origin: they live on production, so a fetch of main needn't bring them.
$tags = @(Invoke-Git ls-remote --tags --refs origin 'v*' | ForEach-Object { ($_ -split 'refs/tags/')[1] })
if ($LASTEXITCODE -ne 0) { throw "Couldn't list origin's tags." }
$now = Get-Date
$build = Get-DevBuild (Get-NextVersion $repoVersion $tags) $now $commit
Write-Host "Dev build $($build.Version) of $($build.Short): $($build.Zip)"

& "$PSScriptRoot/build.ps1" -Version $build.Version
& "$PSScriptRoot/build-esp.ps1"
$notice = "PerkeleRim $($build.Version), an untested dev build of main at commit $commit ($($now.ToString('yyyy-MM-dd'))).`r`n" +
    "It isn't a release and may break saves: back up your My Summer Car and Skyrim saves before you play it.`r`n" +
    "Releases: https://github.com/rackmarten/PerkeleRim/releases/latest"
& "$PSScriptRoot/package.ps1" -Version $build.Name -Notice $notice
$zip = Join-Path $RepoRoot "tools/out/release/$($build.Zip)"
& "$PSScriptRoot/release-notes.ps1" -Zip $zip -DevCommit $commit
$notes = Join-Path $RepoRoot 'tools/out/release/release-notes.md'
$sums = Join-Path $RepoRoot 'tools/out/release/SHA256SUMS.txt'

if ($DryRun) {
    Write-Host "Dry run: nothing uploaded. Would publish $zip, $sums and $notes to the dev pre-release at $commit." -ForegroundColor Yellow
    return
}

# gh finds the repository from the current folder.
Set-Location $RepoRoot
$title = "PerkeleRim dev build $($build.Version) ($($build.Short))"
$existing = gh release view dev --json assets --jq '.assets[].name' 2>$null
if ($LASTEXITCODE -eq 0) {
    # Move the tag first, so the release's source and its zip are the same commit.
    gh api --silent --method PATCH 'repos/{owner}/{repo}/git/refs/tags/dev' -f "sha=$commit" -F force=true
    if ($LASTEXITCODE -ne 0) { throw "Moving the dev tag to $commit failed." }
    Invoke-Checked gh @('release', 'upload', 'dev', $zip, $sums, '--clobber')
    Invoke-Checked gh @('release', 'edit', 'dev', '--title', $title, '--notes-file', $notes, '--prerelease', '--latest=false', '--draft=false')
    foreach ($stale in Get-StaleDevAssets @($existing) $build.Zip) {
        Invoke-Checked gh @('release', 'delete-asset', 'dev', $stale, '--yes')
    }
} else {
    Invoke-Checked gh @('release', 'create', 'dev', $zip, $sums, '--target', $commit, '--title', $title, '--notes-file', $notes, '--prerelease', '--latest=false')
}
Write-Host "Published $($build.Zip) on the dev pre-release: $(gh release view dev --json url --jq .url)"
