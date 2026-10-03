<#
.SYNOPSIS
    Copies the built DLLs into the local game installs (paths from local.props).
    Run scripts/build.ps1 first. Close both games before deploying.
#>
param(
    [ValidateSet('all', 'skse', 'msc')] [string] $Target = 'all',
    [ValidateSet('Release', 'Debug')] [string] $Configuration = 'Release'
)

. "$PSScriptRoot/common.ps1"
$config = Get-LocalConfig

function Copy-Built($source, $destDir) {
    if (-not (Test-Path $source)) { throw "$source not found. Run scripts/build.ps1 first." }
    New-Item -ItemType Directory -Force $destDir | Out-Null
    Copy-Item -Force $source $destDir
    Write-Host "Copied $(Split-Path -Leaf $source) -> $destDir"
}

if ($Target -in 'all', 'skse') {
    $skyrim = Get-RequiredSetting $config 'SkyrimPath'
    $dll = Join-Path $RepoRoot "skse/build/$($Configuration.ToLower())/PerkeleRim.dll"
    Copy-Built $dll (Join-Path $skyrim 'Data/SKSE/Plugins')
    $pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
    if (Test-Path $pdb) { Copy-Built $pdb (Join-Path $skyrim 'Data/SKSE/Plugins') }
}

if ($Target -in 'all', 'msc') {
    $mods = Get-RequiredSetting $config 'MscModsPath'
    Copy-Built (Join-Path $RepoRoot "msc/bin/$Configuration/net35/PerkeleRim.dll") $mods
}
