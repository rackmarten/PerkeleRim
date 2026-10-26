<#
.SYNOPSIS
    The hitbox test with nobody at the keys: puts PerkeleRim_hitboxtest.txt (and, if missing,
    PerkeleRim_autoload.txt) in Skyrim's folder and restarts both games with restart-games.ps1
    (closing them discards anything unsaved; it deploys this checkout's build, -Build builds first),
    so Skyrim loads its newest save. Once it is in, F10 hands Kumpo to MSC unless the save resumes
    that itself. The plugin's driver (skse/src/vehicle/HitboxTest.cpp) then places a bandit archer
    behind the Hayosiko van and a melee bandit just behind it and logs for a minute. This takes two
    screenshots, prints the hitbox lines of PerkeleRim.log, closes both games (-KeepOpen leaves them)
    and takes its flag files away again. The newest save needs Kumpo within 35 m of the van. Exits 1
    when the test didn't finish or Skyrim went away.
.EXAMPLE
    scripts/hitbox-test.ps1
    scripts/hitbox-test.ps1 -Build -KeepOpen
#>
param(
    [switch] $Build,
    [switch] $KeepOpen,
    [int] $TimeoutSeconds = 420
)

. "$PSScriptRoot/common.ps1"
$ErrorActionPreference = 'Stop'
$skyrim = Get-RequiredSetting (Get-LocalConfig) 'SkyrimPath'
$docs = [Environment]::GetFolderPath('MyDocuments')
$log = Join-Path $docs 'My Games\Skyrim Special Edition\SKSE\PerkeleRim.log'
$shots = Join-Path $RepoRoot 'build\hitbox-test'
$flag = Join-Path $skyrim 'PerkeleRim_hitboxtest.txt'
$autoload = Join-Path $skyrim 'PerkeleRim_autoload.txt'
$ownAutoload = -not (Test-Path $autoload)

function Get-Log { if (Test-Path $log) { @(Get-Content $log -Encoding UTF8) } else { @() } }
function Test-Log([string] $Pattern) { [bool]((Get-Log) -match $Pattern) }
function Test-Skyrim { [bool](Get-Process SkyrimSE -ErrorAction SilentlyContinue) }

New-Item -ItemType Directory -Force $shots | Out-Null
Set-Content -Path $flag -Value 'hitbox test'
if ($ownAutoload) { Set-Content -Path $autoload -Value 'hitbox test' }
$ok = $false
try {
    $restart = @{}
    if ($Build) { $restart.Build = $true }
    & "$PSScriptRoot/restart-games.ps1" @restart
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $stage = 'loading'
    $loadedAt = $null
    $armedAt = $null
    $shot = 0
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if (-not (Test-Skyrim)) { Write-Host 'Skyrim went away (a crash?)'; break }
        if ($stage -eq 'loading' -and (Test-Log 'autoload: loading')) { $stage = 'settling'; $loadedAt = Get-Date }
        if ($stage -eq 'settling' -and ((Get-Date) - $loadedAt).TotalSeconds -gt 20) {
            if (-not (Test-Log 'hitbox test: ')) {
                Write-Host 'Handing Kumpo to MSC (F10)'
                & "$PSScriptRoot/skyrim-key.ps1" -Key F10
            }
            $stage = 'waiting'
        }
        if ($stage -eq 'waiting' -and (Test-Log 'hitbox test: (armed|the van .* not run|not run)')) { $stage = 'running'; $armedAt = Get-Date }
        if ($stage -eq 'running') {
            $since = ((Get-Date) - $armedAt).TotalSeconds
            if (($shot -eq 0 -and $since -gt 15) -or ($shot -eq 1 -and $since -gt 40)) {
                $shot++
                & "$PSScriptRoot/skyrim-key.ps1" -Screenshot (Join-Path $shots "shot$shot.png")
            }
            if (Test-Log 'hitbox test: done') { $ok = $true; break }
            if (Test-Log 'hitbox test: .*not run') { break }
        }
    }
    if (-not $ok -and (Get-Date) -ge $deadline) { Write-Host "No 'hitbox test: done' within $TimeoutSeconds s (stage: $stage)" }
} finally {
    Get-Log | Where-Object { $_ -match 'hitbox|player sync: (seated|out of the seat)' } | ForEach-Object { Write-Host $_ }
    if (-not $KeepOpen) {
        Get-Process SkyrimSE, skse64_loader, mysummercar -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    Remove-Item $flag -ErrorAction SilentlyContinue
    if ($ownAutoload) { Remove-Item $autoload -ErrorAction SilentlyContinue }
    Write-Host "Screenshots: $shots"
}
if (-not $ok) { exit 1 }
