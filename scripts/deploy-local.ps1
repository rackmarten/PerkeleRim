<#
.SYNOPSIS
    Copies the built DLLs into the local game installs (paths from local.props), and
    PerkeleRim.esp if scripts/build-esp.ps1 has generated it (also enabling it in plugins.txt).
    Run scripts/build.ps1 first. Close both games before deploying.
#>
param(
    [ValidateSet('all', 'skse', 'msc', 'esp')] [string] $Target = 'all',
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

if ($Target -in 'all', 'esp') {
    $esp = Join-Path $RepoRoot 'tools/out/esp/PerkeleRim.esp'
    if (Test-Path $esp) {
        Copy-Built $esp (Join-Path (Get-RequiredSetting $config 'SkyrimPath') 'Data')

        # Skyrim SE lists active plugins as "*Name.esp", in load order, after the official masters.
        $plugins = Join-Path $env:LOCALAPPDATA 'Skyrim Special Edition/plugins.txt'
        $lines = if (Test-Path $plugins) { @(Get-Content $plugins) } else { @() }
        if ($lines -notcontains '*PerkeleRim.esp') {
            $lines = @($lines | Where-Object { $_ -ne 'PerkeleRim.esp' }) + '*PerkeleRim.esp'
            Set-Content -Path $plugins -Value $lines -Encoding ascii
            Write-Host "Enabled PerkeleRim.esp in $plugins"
        }
    } elseif ($Target -eq 'esp') {
        throw "$esp not found. Run scripts/build-esp.ps1 first."
    } else {
        Write-Host 'No PerkeleRim.esp built (scripts/build-esp.ps1), skipping it.' -ForegroundColor Yellow
    }
}
