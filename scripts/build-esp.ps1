<#
.SYNOPSIS
    Generates PerkeleRim.esp (the garage near Whiterun) from your own Skyrim.esm with tools/esp-gen,
    into tools/out/esp/. With -Deploy, also copies it to Skyrim's Data folder and enables it in
    plugins.txt (deploy-local.ps1 and restart-games.ps1 do that too). Close Skyrim before deploying.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts/build-esp.ps1 -Deploy
#>
param([switch] $Deploy)

. "$PSScriptRoot/common.ps1"
$config = Get-LocalConfig
$data = Join-Path (Get-RequiredSetting $config 'SkyrimPath') 'Data'
$esp = Join-Path $RepoRoot 'tools/out/esp/PerkeleRim.esp'

dotnet run --project (Join-Path $RepoRoot 'tools/esp-gen') -c Release -- --data $data --out $esp
if ($LASTEXITCODE -ne 0) { throw "esp-gen failed ($LASTEXITCODE)" }

if ($Deploy) { & "$PSScriptRoot/deploy-local.ps1" -Target esp }
