# Shared helpers for the build/deploy/log scripts. Dot-source it: . "$PSScriptRoot/common.ps1"

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot

# Reads local.props (MSBuild XML) into a hashtable. Copy local.props.example to create it.
function Get-LocalConfig {
    $path = Join-Path $RepoRoot 'local.props'
    if (-not (Test-Path $path)) {
        throw "local.props not found. Copy local.props.example to local.props in the repo root and set your game paths."
    }
    $config = @{}
    foreach ($group in ([xml](Get-Content -Raw $path)).Project.PropertyGroup) {
        foreach ($node in $group.ChildNodes) {
            if ($node.NodeType -eq 'Element') { $config[$node.LocalName] = $node.InnerText.Trim() }
        }
    }
    return $config
}

function Get-RequiredSetting($config, $name) {
    if (-not $config[$name]) { throw "$name is not set in local.props." }
    return $config[$name]
}

# blender.exe: BlenderPath from local.props, else the newest install under Program Files.
function Get-Blender {
    $config = if (Test-Path (Join-Path $RepoRoot 'local.props')) { Get-LocalConfig } else { @{} }
    if ($config['BlenderPath']) { return $config['BlenderPath'] }
    $found = Get-ChildItem (Join-Path $env:ProgramFiles 'Blender Foundation') -Filter blender.exe -Recurse -Depth 1 -ErrorAction SilentlyContinue |
        Sort-Object { $_.Directory.Name } | Select-Object -Last 1
    if (-not $found) { throw "Blender not found. Install it (winget install BlenderFoundation.Blender) or set BlenderPath in local.props." }
    return $found.FullName
}

# Enters the x64 MSVC developer environment of the newest VS / Build Tools install with the C++ tools.
function Enter-DevShell {
    if ($env:VSCMD_ARG_TGT_ARCH -eq 'x64') { return }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found. Install Build Tools for Visual Studio 2022 (docs/DEV_SETUP.md)." }
    $vsPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vsPath) { throw "No Visual Studio install with the C++ x64 tools found. Add the 'Desktop development with C++' workload." }
    # A stray quote in PATH makes VsDevCmd.bat fail on "(x86)" entries; strip quotes for this process only.
    $env:Path = $env:Path -replace '"', ''
    Import-Module (Join-Path $vsPath 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
    Enter-VsDevShell -VsInstallPath $vsPath -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
}
