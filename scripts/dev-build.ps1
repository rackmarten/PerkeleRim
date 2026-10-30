<#
.SYNOPSIS
    Pure helpers for scripts/publish-dev.ps1 (the rolling `dev` pre-release): the next version, the
    dev build's names, the checkout guard and which old assets go. No git, gh or files here, so
    scripts/test.ps1 covers them (scripts/tests). Dot-source it. Runs on Windows PowerShell 5.1 too.
#>

# The version main is heading for. main's version is the last release's until the release pull request
# bumps it (docs/DEV_SETUP.md, "Cutting a release"), and main's next release is a minor one (patches are
# production's hotfixes), so a version that already has its tag becomes the next minor.
function Get-NextVersion([string] $Version, [string[]] $Tags) {
    if ($Tags -notcontains "v$Version") { return $Version }
    $v = [version] $Version
    return "$($v.Major).$($v.Minor + 1).0"
}

# The dev build of $NextVersion at $Commit on $Date: Version is the semver pre-release the MSC mod
# carries (its ProductVersion adds +<commit>), Name the zip's name and top folder after PerkeleRim-.
function Get-DevBuild([string] $NextVersion, [datetime] $Date, [string] $Commit) {
    $day = $Date.ToString('yyyyMMdd')
    $short = $Commit.Substring(0, 7)
    $name = "dev-$day-$short"
    return [pscustomobject] @{
        Version = "$NextVersion-dev.$day"
        Name    = $name
        Zip     = "PerkeleRim-$name.zip"
        Short   = $short
    }
}

# Why this checkout can't be published, if it can't: dev builds come from a clean main that is origin's.
# $Status is `git status --porcelain`'s lines.
function Get-CheckoutProblems([string] $Branch, [string[]] $Status, [string] $Head, [string] $OriginMain) {
    $problems = @()
    if ($Branch -ne 'main') { $problems += "The checkout is on '$Branch', not main: git switch main" }
    $changed = @($Status | Where-Object { $_ })
    if ($changed) { $problems += "The checkout has local changes (commit, stash or remove them):`n    $($changed -join "`n    ")" }
    if (-not $OriginMain) {
        $problems += 'origin/main not found: git fetch origin'
    } elseif ($Head -ne $OriginMain) {
        $problems += "HEAD ($($Head.Substring(0, 7))) isn't origin/main ($($OriginMain.Substring(0, 7))): git pull --ff-only, or push main first"
    }
    return $problems
}

# The `dev` release's assets to delete once $Keep is up: older dev zips. SHA256SUMS.txt is replaced, not deleted.
function Get-StaleDevAssets([string[]] $Assets, [string] $Keep) {
    return @($Assets | Where-Object { $_ -match '^PerkeleRim-dev-.+\.zip$' -and $_ -ne $Keep })
}
