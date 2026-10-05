<#
.SYNOPSIS
    Downloads the mesh conversion tools into tools/.deps (gitignored): AssetStudioModCLI, PyNifly
    (Blender add-on, loaded from here, never installed into your Blender profile) and texconv.
    Blender itself is a normal install (winget install BlenderFoundation.Blender).
    Safe to re-run; skips what is already there.
#>
. "$PSScriptRoot/../scripts/common.ps1"

$deps = Join-Path $PSScriptRoot '.deps'
New-Item -ItemType Directory -Force $deps | Out-Null

# Pinned versions, bump on purpose.
$tools = @(
    @{ Name = 'AssetStudioModCLI'; Url = 'https://github.com/aelurum/AssetStudioMod/releases/download/v0.19.0/AssetStudioModCLI_net472_win32_64.zip'; Check = 'AssetStudioModCLI/AssetStudioModCLI.exe' },
    @{ Name = 'pynifly'; Url = 'https://github.com/BadDogSkyrim/PyNifly/releases/download/V29.1.0/io_scene_nifly.zip'; Check = 'pynifly/io_scene_nifly/__init__.py' },
    @{ Name = 'texconv'; Url = 'https://github.com/microsoft/DirectXTex/releases/download/may2026/texconv.exe'; Check = 'texconv/texconv.exe' }
)

$ProgressPreference = 'SilentlyContinue'
foreach ($tool in $tools) {
    if (Test-Path (Join-Path $deps $tool.Check)) { Write-Host "$($tool.Name): already there"; continue }
    $dir = Join-Path $deps $tool.Name
    New-Item -ItemType Directory -Force $dir | Out-Null
    $file = Join-Path $dir (Split-Path -Leaf $tool.Url)
    Write-Host "$($tool.Name): downloading $($tool.Url)"
    Invoke-WebRequest -Uri $tool.Url -OutFile $file
    if ($file.EndsWith('.zip')) {
        Expand-Archive -Force $file $dir
        Remove-Item $file
        # Some zips wrap everything in one folder; flatten it.
        $inner = @(Get-ChildItem $dir)
        if ($inner.Count -eq 1 -and $inner[0].PSIsContainer -and $inner[0].Name -ne 'io_scene_nifly') {
            Get-ChildItem $inner[0].FullName | Move-Item -Destination $dir
            Remove-Item $inner[0].FullName
        }
    }
    if (-not (Test-Path (Join-Path $deps $tool.Check))) { throw "$($tool.Name): $($tool.Check) not found after download." }
}

$blender = Get-Blender
Write-Host "Blender: $blender"
