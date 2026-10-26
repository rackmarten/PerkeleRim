<#
.SYNOPSIS
    The hitbox test with nobody at the keys: adds [Debug] HitboxTest=1 (and AutoloadSave=<-Save>) to
    PerkeleRim.ini, puts PerkeleRim_autoload.txt in both games' folders where missing and restarts them
    with restart-games.ps1 (closing them discards anything unsaved; it deploys this checkout's build,
    -Build builds first), so they load their newest saves (Skyrim the -Save one). Once Skyrim is in,
    F10 hands Kumpo to MSC unless the save resumes that itself, and ] calls the van in front of him
    (again after 30 s if the test hasn't started). The plugin's driver (skse/src/vehicle/HitboxTest.cpp)
    then places a bandit archer behind the Hayosiko van and a melee bandit just behind it and logs for
    a minute. This takes two screenshots, prints the hitbox lines of PerkeleRim.log, closes both games
    (-KeepOpen leaves them) and takes its ini lines and flag files away again. Any save with Kumpo
    outdoors works (the van can't be called into an interior). Exits 1 when the test didn't finish or
    Skyrim went away.
.EXAMPLE
    scripts/hitbox-test.ps1
    scripts/hitbox-test.ps1 -Build -KeepOpen
    scripts/hitbox-test.ps1 -Save Save10_2494F6F2_1_4B756D706F_Tamriel_000116_20261026170454_1_1
#>
param(
    [switch] $Build,
    [switch] $KeepOpen,
    [string] $Save = '',  # AutoloadSave: a Skyrim save's file name instead of the newest save
    [int] $TimeoutSeconds = 420
)

. "$PSScriptRoot/common.ps1"
$ErrorActionPreference = 'Stop'
$config = Get-LocalConfig
$skyrim = Get-RequiredSetting $config 'SkyrimPath'
$msc = Get-RequiredSetting $config 'MscPath'
$docs = [Environment]::GetFolderPath('MyDocuments')
$log = Join-Path $docs 'My Games\Skyrim Special Edition\SKSE\PerkeleRim.log'
$ini = Join-Path $skyrim 'Data\SKSE\Plugins\PerkeleRim.ini'
$shots = Join-Path $RepoRoot 'build\hitbox-test'
$utf8 = New-Object System.Text.UTF8Encoding $false
if ($Save) {
    $Save = $Save -replace '\.ess$', ''
    $saveFile = Join-Path $docs "My Games\Skyrim Special Edition\Saves\$Save.ess"
    if (-not (Test-Path $saveFile)) { throw "No save $saveFile" }
}

function Get-Log { if (Test-Path $log) { @(Get-Content $log -Encoding UTF8) } else { @() } }
function Test-Log([string] $Pattern) { [bool]((Get-Log) -match $Pattern) }
function Test-Skyrim { [bool](Get-Process SkyrimSE -ErrorAction SilentlyContinue) }

# The ini without this test's lines (the rest stays).
function Remove-TestLines {
    if (-not (Test-Path $ini)) { return }
    $kept = @(Get-Content $ini -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(\[Debug\]|HitboxTest|AutoloadSave)' })
    while ($kept.Count -gt 0 -and $kept[-1].Trim() -eq '') { $kept = @($kept | Select-Object -SkipLast 1) }
    [IO.File]::WriteAllLines($ini, [string[]] $kept, $utf8)
}

New-Item -ItemType Directory -Force $shots | Out-Null
$flags = @((Join-Path $skyrim 'PerkeleRim_autoload.txt'), (Join-Path $msc 'PerkeleRim_autoload.txt'))
$made = @($flags | Where-Object { -not (Test-Path $_) })
$ok = $false
try {
    $made | ForEach-Object { New-Item -ItemType File $_ | Out-Null }
    Remove-TestLines
    $lines = @('', '[Debug]', 'HitboxTest=1')
    if ($Save) { $lines += "AutoloadSave=$Save" }
    New-Item -ItemType Directory -Force (Split-Path $ini) | Out-Null
    [IO.File]::AppendAllLines($ini, [string[]] $lines, $utf8)
    $restart = @{}
    if ($Build) { $restart.Build = $true }
    & "$PSScriptRoot/restart-games.ps1" @restart
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $stage = 'loading'
    $loadedAt = $null
    $armedAt = $null
    $calledAt = $null
    $recalled = $false
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
        if ($stage -eq 'waiting' -and (Test-Log 'hitbox test: .*waiting for the van')) {
            Write-Host 'Calling the van (])'
            & "$PSScriptRoot/skyrim-key.ps1" -Key RightBracket
            $stage = 'calling'
            $calledAt = Get-Date
        }
        if ($stage -eq 'calling' -and -not $recalled -and ((Get-Date) - $calledAt).TotalSeconds -gt 30 -and -not (Test-Log 'hitbox test: armed')) {
            Write-Host 'Calling the van again (])'
            & "$PSScriptRoot/skyrim-key.ps1" -Key RightBracket
            $recalled = $true
        }
        if ($stage -eq 'calling' -and (Test-Log 'hitbox test: (armed|.*not run)')) { $stage = 'running'; $armedAt = Get-Date }
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
    Remove-TestLines
    $made | ForEach-Object { Remove-Item $_ -ErrorAction SilentlyContinue }
    Write-Host "Screenshots: $shots"
}
if (-not $ok) { exit 1 }
