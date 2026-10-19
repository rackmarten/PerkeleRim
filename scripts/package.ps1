<#
.SYNOPSIS
    Builds the player release zip, tools/out/release/PerkeleRim-<version>.zip: the launcher
    (launcher/), the built mod files under files/, and the starter saves from
    launcher/saves/MSC and launcher/saves/Skyrim (scripts/starter-save.ps1) if they are there. Run scripts/build.ps1 and scripts/build-esp.ps1 first.
.EXAMPLE
    scripts/package.ps1
#>
param([string] $Version)

. "$PSScriptRoot/common.ps1"

if (-not $Version) {
    $Version = (Select-String -Path (Join-Path $RepoRoot 'skse/CMakeLists.txt') -Pattern '^\s*VERSION\s+(\S+)').Matches[0].Groups[1].Value
}
$out = Join-Path $RepoRoot 'tools/out/release'
$stage = Join-Path $out "PerkeleRim-$Version"
$zip = "$stage.zip"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

function Add-File($source, $destDir) {
    if (-not (Test-Path $source)) { throw "$source not found. Run scripts/build.ps1 and scripts/build-esp.ps1 first." }
    $dir = Join-Path $stage $destDir
    New-Item -ItemType Directory -Force $dir | Out-Null
    Copy-Item $source $dir
}

Add-File (Join-Path $RepoRoot 'launcher/PerkeleRim.ps1') '.'
Add-File (Join-Path $RepoRoot 'launcher/Start PerkeleRim.bat') '.'
Add-File (Join-Path $RepoRoot 'launcher/Report a bug.bat') '.'
Add-File (Join-Path $RepoRoot 'skse/build/release/PerkeleRim.dll') 'files/Skyrim/Data/SKSE/Plugins'
Add-File (Join-Path $RepoRoot 'tools/out/esp/PerkeleRim.esp') 'files/Skyrim/Data'
Add-File (Join-Path $RepoRoot 'msc/bin/Release/net35/PerkeleRim.dll') 'files/MSC/Mods'
Add-File (Join-Path $RepoRoot 'LICENSE') '.'

foreach ($game in 'MSC', 'Skyrim') {
    $saves = Join-Path $RepoRoot "launcher/saves/$game"
    if (Test-Path $saves) {
        Copy-Item -Recurse $saves (New-Item -ItemType Directory -Force (Join-Path $stage 'saves')).FullName
    } else {
        Write-Host "No launcher/saves/$game (scripts/starter-save.ps1), the zip has no starter $game save." -ForegroundColor Yellow
    }
}

if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $stage -DestinationPath $zip
Write-Host "Packaged $zip"
