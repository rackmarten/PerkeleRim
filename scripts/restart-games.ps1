<#
.SYNOPSIS
    The whole in-game test loop in one command: closes both games, optionally builds, deploys,
    starts My Summer Car (launch-msc.ps1, continues the save if <MSC>/PerkeleRim_autoload.txt
    exists), then starts Skyrim through SKSE and waits until both sides log "link up".
    Killing the games discards anything unsaved in them.
.EXAMPLE
    scripts/restart-games.ps1           # deploy what's built and restart
    scripts/restart-games.ps1 -Build    # build first
    powershell -ExecutionPolicy Bypass -File scripts/restart-games.ps1 -Build
#>
param(
    [switch] $Build,
    [int] $LinkTimeoutSeconds = 180
)

. "$PSScriptRoot/common.ps1"
$config = Get-LocalConfig
$skyrim = Get-RequiredSetting $config 'SkyrimPath'
$msc = Get-RequiredSetting $config 'MscPath'

if ($Build) { & "$PSScriptRoot/build.ps1" }

$running = Get-Process mysummercar, SkyrimSE, skse64_loader -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Closing $(($running.Name | Sort-Object -Unique) -join ', ')"
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue
}

# Windows can keep the plugin DLL locked for a few seconds after the process is gone.
for ($attempt = 1; ; $attempt++) {
    try { & "$PSScriptRoot/deploy-local.ps1"; break }
    catch {
        if ($attempt -ge 10) { throw }
        Start-Sleep -Seconds 2
    }
}

$skseLog = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Skyrim Special Edition\SKSE\PerkeleRim.log'
$mscLog = Join-Path $msc 'output_log.txt'
$started = Get-Date

if (-not (Test-Path (Join-Path $msc 'PerkeleRim_autoload.txt'))) {
    Write-Host 'No PerkeleRim_autoload.txt: MSC will stay at its main menu.' -ForegroundColor Yellow
}
& "$PSScriptRoot/launch-msc.ps1"

Write-Host 'Starting Skyrim (SKSE)'
Start-Process -WorkingDirectory $skyrim (Join-Path $skyrim 'skse64_loader.exe')

# Both logs are rewritten when their game starts, so a "link up" newer than this run is ours.
function Test-LinkUp($path) {
    (Test-Path $path) -and (Get-Item $path).LastWriteTime -gt $started -and
        (Select-String -Path $path -Pattern 'link up' -SimpleMatch -Quiet)
}
$deadline = $started.AddSeconds($LinkTimeoutSeconds)
while ((Get-Date) -lt $deadline) {
    if ((Test-LinkUp $skseLog) -and (Test-LinkUp $mscLog)) {
        Write-Host 'Link up on both sides. Load a save in Skyrim and press F10.' -ForegroundColor Green
        return
    }
    Start-Sleep -Seconds 2
}
throw "No 'link up' in both logs after $LinkTimeoutSeconds s. Check scripts/logs.ps1."
