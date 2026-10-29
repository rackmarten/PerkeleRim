<#
.SYNOPSIS
    Checks that a release zip (scripts/package.ps1) matches a release tag: its name and top folder
    carry the tag's version, it has every file package.ps1 puts in, the MSC mod's version and the
    commit it was built from (its ProductVersion, X.Y.Z+<commit>) are the tag's, and the launcher,
    LICENSE and starter saves are byte for byte the ones in -Source, a checkout of the tag. The
    zip is built locally (docs/DEV_SETUP.md, "Cutting a release"), so this is how the release
    workflow (.github/workflows/release.yml) ties it to the tag. Windows (FileVersionInfo).
    Throws on the first run of problems; with -WarnOnly, only warns (the workflow's dry run).
.EXAMPLE
    scripts/release-check.ps1 -Zip tools/out/release/PerkeleRim-0.2.0.zip -Tag v0.2.0
#>
param(
    [Parameter(Mandatory)] [string] $Zip,
    [Parameter(Mandatory)] [string] $Tag,
    [string] $Source,
    [switch] $WarnOnly
)

. "$PSScriptRoot/common.ps1"
if (-not $Source) { $Source = $RepoRoot }
$Source = (Resolve-Path -LiteralPath $Source).Path
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw "$Tag isn't a release tag (vX.Y.Z); dev builds (scripts/publish-dev.ps1) aren't checked." }
$version = $Tag -replace '^v', ''
$top = "PerkeleRim-$version"
$problems = @()

$zipName = Split-Path -Leaf $Zip
if ($zipName -ne "$top.zip") { $problems += "The zip is $zipName, tag $Tag wants $top.zip." }

$commit = git -C $Source rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "$Source isn't a git checkout." }

$extract = Join-Path ([IO.Path]::GetTempPath()) "perkelerim-release-check-$PID"
if (Test-Path $extract) { Remove-Item -Recurse -Force $extract }
Expand-Archive -LiteralPath $Zip -DestinationPath $extract
try {
    $folders = @(Get-ChildItem -LiteralPath $extract)
    if ($folders.Count -ne 1 -or $folders[0].Name -ne $top) {
        $problems += "The zip's top level is '$($folders.Name -join "', '")', not the single folder $top."
    }
    $root = $folders[0].FullName

    # What scripts/package.ps1 puts in; the saves come from launcher/saves.
    $sameAsSource = [ordered] @{
        'PerkeleRim.ps1'       = 'launcher/PerkeleRim.ps1'
        'Start PerkeleRim.bat' = 'launcher/Start PerkeleRim.bat'
        'Report a bug.bat'     = 'launcher/Report a bug.bat'
        'checks.ps1'           = 'launcher/checks.ps1'
        'install.ps1'          = 'launcher/install.ps1'
        'wizard.ps1'           = 'launcher/wizard.ps1'
        'LICENSE'              = 'LICENSE'
    }
    $savesDir = Join-Path $Source 'launcher/saves'
    if (Test-Path $savesDir) {
        foreach ($file in Get-ChildItem -LiteralPath $savesDir -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath($savesDir, $file.FullName) -replace '\\', '/'
            $sameAsSource["saves/$relative"] = "launcher/saves/$relative"
        }
    }
    foreach ($entry in $sameAsSource.GetEnumerator()) {
        $inZip = Join-Path $root $entry.Key
        if (-not (Test-Path -LiteralPath $inZip)) { $problems += "$($entry.Key) is missing."; continue }
        $hash = (Get-FileHash -LiteralPath $inZip -Algorithm SHA256).Hash
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $Source $entry.Value) -Algorithm SHA256).Hash) {
            $problems += "$($entry.Key) differs from $($entry.Value) at $Tag."
        }
    }

    foreach ($built in 'files/Skyrim/Data/SKSE/Plugins/PerkeleRim.dll', 'files/Skyrim/Data/Interface/PerkeleRim/MscMenu.swf', 'files/Skyrim/Data/PerkeleRim.esp') {
        if (-not (Test-Path -LiteralPath (Join-Path $root $built))) { $problems += "$built is missing." }
    }

    # The MSC mod's ProductVersion is the csproj's Version plus the commit it was built from.
    $mscDll = Join-Path $root 'files/MSC/Mods/PerkeleRim.dll'
    if (Test-Path -LiteralPath $mscDll) {
        $product = [Diagnostics.FileVersionInfo]::GetVersionInfo($mscDll).ProductVersion
        $builtVersion, $builtCommit = $product -split '\+', 2
        if ($builtVersion -ne $version) { $problems += "The MSC mod is version $builtVersion, not $version." }
        if ($builtCommit -ne $commit) { $problems += "The MSC mod was built from commit '$builtCommit', not $Tag's $commit." }
    } else {
        $problems += 'files/MSC/Mods/PerkeleRim.dll is missing.'
    }
} finally {
    Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
}

if (-not $problems) {
    Write-Host "$zipName matches $Tag ($commit)."
    return
}
if ($WarnOnly) {
    foreach ($problem in $problems) { Write-Host "::warning::$problem" }
    return
}
throw "$zipName doesn't match $Tag`n  $($problems -join "`n  ")"
