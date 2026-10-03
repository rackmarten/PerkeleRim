<#
.SYNOPSIS
    Builds the SKSE plugin (CMake + Ninja + vcpkg) and the MSCLoader mod (dotnet build).
.EXAMPLE
    scripts/build.ps1                       # both, Release
    scripts/build.ps1 -Target skse -Configuration Debug
    scripts/build.ps1 -Target msc -UseStubs # C# mod against stub DLLs, no game install needed
    powershell.exe -File scripts/build.ps1  # from WSL
#>
param(
    [ValidateSet('all', 'skse', 'msc')] [string] $Target = 'all',
    [ValidateSet('Release', 'Debug')] [string] $Configuration = 'Release',
    [switch] $UseStubs
)

. "$PSScriptRoot/common.ps1"

function Invoke-Checked {
    param([string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

if ($Target -in 'all', 'skse') {
    Enter-DevShell
    if (-not $env:VCPKG_ROOT -and $env:VCPKG_INSTALLATION_ROOT) { $env:VCPKG_ROOT = $env:VCPKG_INSTALLATION_ROOT }  # GitHub runners
    if (-not $env:VCPKG_ROOT) { throw "VCPKG_ROOT is not set. Install vcpkg (docs/DEV_SETUP.md) or use the copy bundled with Build Tools." }
    $preset = $Configuration.ToLower()
    $skse = Join-Path $RepoRoot 'skse'
    if (-not (Test-Path (Join-Path $skse 'extern/CommonLibSSE-NG/CMakeLists.txt'))) {
        Invoke-Checked git @('-C', $RepoRoot, 'submodule', 'update', '--init', '--recursive')
    }
    Push-Location $skse
    try {
        Invoke-Checked cmake @('--preset', $preset)
        Invoke-Checked cmake @('--build', '--preset', $preset)
    } finally { Pop-Location }
    Write-Host "SKSE plugin: skse/build/$preset/SkyrimSummerCar.dll"
}

if ($Target -in 'all', 'msc') {
    $dotnetArgs = @('build', (Join-Path $RepoRoot 'msc/SkyrimSummerCar.csproj'), '-c', $Configuration, '-nologo')
    if ($UseStubs) { $dotnetArgs += '-p:UseStubs=true' }
    Invoke-Checked dotnet $dotnetArgs
    $outDir = if ($UseStubs) { "msc/bin/stubs/$Configuration" } else { "msc/bin/$Configuration" }
    Write-Host "MSCLoader mod: $outDir/net35/SkyrimSummerCar.dll"
}
