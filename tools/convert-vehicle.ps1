<#
.SYNOPSIS
    Converts an MSC vehicle from your own My Summer Car install into a Skyrim SE NIF plus DDS textures,
    under tools/out/<vehicle>/Data (gitignored, never commit it). -Deploy copies that into Skyrim's Data.
    Run tools/setup.ps1 once first.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/convert-vehicle.ps1 -Deploy
#>
param(
    [string] $Vehicle = 'hayosiko',
    [switch] $Deploy
)

. "$PSScriptRoot/../scripts/common.ps1"
$config = Get-LocalConfig
$deps = Join-Path $PSScriptRoot '.deps'
$settingsPath = Join-Path $PSScriptRoot "nif/$Vehicle.json"
$settings = Get-Content -Raw $settingsPath | ConvertFrom-Json

$out = Join-Path $PSScriptRoot "out/$Vehicle"
$raw = Join-Path $out 'raw'
$data = Join-Path $out 'Data'
$texDir = Join-Path $data $settings.textures
$nif = Join-Path $data $settings.nif

# 1. Unity scene object -> model.json + PNGs (AssetStudio libraries).
$exporter = Join-Path $deps 'AssetStudioModCLI/UnityExport.exe'
if (-not (Test-Path $exporter)) {
    dotnet build (Join-Path $PSScriptRoot 'unity-export/UnityExport.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'UnityExport build failed.' }
}
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
$mscData = Join-Path (Get-RequiredSetting $config 'MscPath') 'mysummercar_Data'
& $exporter $mscData $settings.unity_object $raw | Where-Object { $_ -notmatch '^\[\d' }
if ($LASTEXITCODE -ne 0) { throw 'UnityExport failed.' }

# 2. PNG -> DDS with mipmaps.
New-Item -ItemType Directory -Force $texDir | Out-Null
$pngs = Get-ChildItem $raw -Filter *.png | ForEach-Object FullName
& (Join-Path $deps 'texconv/texconv.exe') -nologo -y -m 0 -f BC3_UNORM -o $texDir @pngs | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'texconv failed.' }

# 3. Blender + PyNifly -> NIF. Blender logs to stderr, which Windows PowerShell would turn into errors.
$ErrorActionPreference = 'Continue'
& (Get-Blender) -b --factory-startup --python (Join-Path $PSScriptRoot 'nif/build_nif.py') -- `
    (Join-Path $raw 'model.json') $nif $texDir $settingsPath 2>&1 | ForEach-Object { "$_" } |
    Where-Object { $_ -match 'build_nif|Error|Traceback|WARNING|line \d' }
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $nif)) { throw 'No NIF written, see the Blender output above.' }
Write-Host "Built $nif"

if ($Deploy) {
    $skyrimData = Join-Path (Get-RequiredSetting $config 'SkyrimPath') 'Data'
    Copy-Item -Recurse -Force (Join-Path $data '*') $skyrimData
    Write-Host "Copied $data -> $skyrimData"
}
