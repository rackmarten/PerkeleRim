<#
.SYNOPSIS
    Runs the unit tests: the C++ protocol tests (CMake + doctest, ctest) and the C# link tests
    (dotnet test). Neither needs the games installed.
.EXAMPLE
    scripts/test.ps1                  # both
    scripts/test.ps1 -Target cpp
    powershell.exe -File scripts/test.ps1   # from WSL
#>
param(
    [ValidateSet('all', 'cpp', 'cs')] [string] $Target = 'all'
)

. "$PSScriptRoot/common.ps1"

function Invoke-Checked {
    param([string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

if ($Target -in 'all', 'cpp') {
    Enter-DevShell
    if (-not $env:VCPKG_ROOT -and $env:VCPKG_INSTALLATION_ROOT) { $env:VCPKG_ROOT = $env:VCPKG_INSTALLATION_ROOT }  # GitHub runners
    if (-not $env:VCPKG_ROOT) { throw "VCPKG_ROOT is not set. Install vcpkg (docs/DEV_SETUP.md) or use the copy bundled with Build Tools." }
    Push-Location (Join-Path $RepoRoot 'protocol')
    try {
        Invoke-Checked cmake @('--preset', 'tests')
        Invoke-Checked cmake @('--build', '--preset', 'tests')
        Invoke-Checked ctest @('--preset', 'tests')
    } finally { Pop-Location }
}

if ($Target -in 'all', 'cs') {
    Invoke-Checked dotnet @('test', (Join-Path $RepoRoot 'msc/tests/PerkeleRim.Tests.csproj'), '-nologo')
}
