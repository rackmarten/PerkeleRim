<#
.SYNOPSIS
    Snapshots and restores My Summer Car save data, so every test can start from the same state.
    Snapshots live outside the repo in %LOCALAPPDATA%\PerkeleRim\msc-saves\<name>.
    Close the game before restoring. If Steam Cloud is on for MSC, it may sync an older save back.
.EXAMPLE
    scripts/msc-save.ps1 snapshot baseline
    scripts/msc-save.ps1 restore baseline
    scripts/msc-save.ps1 list
#>
param(
    [Parameter(Mandatory)] [ValidateSet('snapshot', 'restore', 'list')] [string] $Action,
    [string] $Name = 'baseline'
)

$ErrorActionPreference = 'Stop'
$saveDir = Join-Path $env:USERPROFILE 'AppData\LocalLow\Amistech\My Summer Car'
$store = Join-Path $env:LOCALAPPDATA 'PerkeleRim\msc-saves'
$snap = Join-Path $store $Name
# Settings files stay as they are; everything else is save data.
$keep = @('options.txt', 'Mods.txt', 'steam_autocloud.vdf')

switch ($Action) {
    'list' {
        if (Test-Path $store) { Get-ChildItem $store -Directory | Select-Object Name, LastWriteTime }
    }
    'snapshot' {
        if (-not (Test-Path (Join-Path $saveDir 'defaultES2File.txt'))) { throw "No save found in $saveDir. Save in-game first." }
        if (Test-Path $snap) { Remove-Item -Recurse -Force $snap }
        New-Item -ItemType Directory -Force $snap | Out-Null
        Get-ChildItem $saveDir -File | Where-Object { $_.Name -notin $keep } | Copy-Item -Destination $snap
        Write-Host "Snapshot '$Name' -> $snap"
    }
    'restore' {
        if (Get-Process mysummercar -ErrorAction SilentlyContinue) { throw 'Close My Summer Car first.' }
        if (-not (Test-Path $snap)) { throw "No snapshot '$Name' in $store." }
        Get-ChildItem $saveDir -File | Where-Object { $_.Name -notin $keep } | Remove-Item -Force
        Copy-Item (Join-Path $snap '*') $saveDir
        Write-Host "Restored '$Name' -> $saveDir"
    }
}
