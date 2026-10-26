<#
.SYNOPSIS
    The hit test: PerkeleRim's attacks on Skyrim's actors with nobody at the keys. Adds [Debug] HitTest
    to PerkeleRim.ini and restarts both games with restart-games.ps1 (closing them discards anything
    unsaved; it deploys this checkout's build, -Build builds first), so they load their newest saves.
    The SKSE plugin's driver (skse/src/debug/HitTest.cpp) then hands Kumpo to MSC and runs the
    scenarios of protocol/cpp/hit_test.h: punch (sober), drunk (MSC's drunk level 2) and bottle (an
    empty bottle thrown at full charge), each at a giant placed just ahead with its AI off. Meanwhile
    this watches for a crash (a new crash-*.log), either game gone and a hang (PerkeleRim.log still for
    -HangSeconds). At the end it prints the driver's log lines and the CSV (Documents/My Games/
    PerkeleRim/hittest.csv), closes both games and takes [Debug] out of the ini again. Exits 1 when a
    row failed or the test didn't finish. Nothing is saved, in either game.
.EXAMPLE
    scripts/hit-test.ps1
    scripts/hit-test.ps1 -Scenarios punch,drunk -Build
    scripts/hit-test.ps1 -KeepOpen          # leave both games running at the end, to look around
#>
param(
    [string] $Scenarios = 'all',
    [switch] $Build,
    [string] $Target = '',  # HitTestTarget: an actor base's form id in hex instead of Skyrim.esm's LvlGiant
    [int] $TimeoutSeconds = 600,
    [int] $HangSeconds = 120,
    [switch] $KeepOpen
)

. "$PSScriptRoot/common.ps1"
$ErrorActionPreference = 'Stop'
$config = Get-LocalConfig
$skyrim = Get-RequiredSetting $config 'SkyrimPath'
$msc = Get-RequiredSetting $config 'MscPath'
$docs = [Environment]::GetFolderPath('MyDocuments')
$skseDir = Join-Path $docs 'My Games\Skyrim Special Edition\SKSE'
$log = Join-Path $skseDir 'PerkeleRim.log'
$csv = Join-Path $docs 'My Games\PerkeleRim\hittest.csv'
$ini = Join-Path $skyrim 'Data\SKSE\Plugins\PerkeleRim.ini'
$utf8 = New-Object System.Text.UTF8Encoding $false

# The ini without the hit test's lines (the rest, such as ScreenshotDir, stays).
function Remove-HitTestLines {
    if (-not (Test-Path $ini)) { return }
    $kept = @(Get-Content $ini -Encoding UTF8 | Where-Object { $_ -notmatch '^\s*(\[Debug\]|HitTest)' })
    while ($kept.Count -gt 0 -and $kept[-1].Trim() -eq '') { $kept = @($kept | Select-Object -SkipLast 1) }
    [IO.File]::WriteAllLines($ini, [string[]] $kept, $utf8)
}

function Stop-Games {
    for ($i = 0; $i -lt 12; $i++) {
        $left = Get-Process SkyrimSE, skse64_loader, mysummercar -ErrorAction SilentlyContinue
        if (-not $left) { return }
        $left | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 5
    }
}

# Both games load their newest save at their main menu with this flag file (Autoload, the spike).
$flags = @((Join-Path $skyrim 'PerkeleRim_autoload.txt'), (Join-Path $msc 'PerkeleRim_autoload.txt'))
$made = @($flags | Where-Object { -not (Test-Path $_) })
$made | ForEach-Object { New-Item -ItemType File $_ | Out-Null }

Remove-HitTestLines
$lines = @('', '[Debug]', "HitTest=$Scenarios")
if ($Target) { $lines += "HitTestTarget=$Target" }
New-Item -ItemType Directory -Force (Split-Path $ini) | Out-Null
[IO.File]::AppendAllLines($ini, [string[]] $lines, $utf8)
if (Test-Path $csv) { Remove-Item $csv }

$known = @(Get-ChildItem $skseDir -Filter 'crash-*.log' -ErrorAction SilentlyContinue | ForEach-Object Name)
$finished = $false
$problem = $null
try {
    & "$PSScriptRoot/restart-games.ps1" -Build:$Build
    $started = Get-Date
    Write-Host "Hit test '$Scenarios' running; don't touch either game." -ForegroundColor Cyan
    while ($true) {
        Start-Sleep -Seconds 5
        if (Select-String -Path $log -Pattern 'hit test: (done|stopped)' -Quiet) { $finished = $true; break }
        $crash = Get-ChildItem $skseDir -Filter 'crash-*.log' | Where-Object { $known -notcontains $_.Name } | Select-Object -First 1
        if ($crash) { $problem = "Skyrim crashed: $($crash.FullName)"; break }
        if (-not (Get-Process SkyrimSE -ErrorAction SilentlyContinue)) { $problem = 'Skyrim is gone, without a crash log'; break }
        if (-not (Get-Process mysummercar -ErrorAction SilentlyContinue)) { $problem = 'My Summer Car is gone (its output_log.txt may say why)'; break }
        $still = ((Get-Date) - (Get-Item $log).LastWriteTime).TotalSeconds
        if ($still -gt $HangSeconds) { $problem = "a hang: PerkeleRim.log still for $([int] $still) s"; break }
        if (((Get-Date) - $started).TotalSeconds -gt $TimeoutSeconds) { $problem = "no end within $TimeoutSeconds s"; break }
    }
} finally {
    if (-not $KeepOpen) { Stop-Games }
    Remove-HitTestLines
    $made | ForEach-Object { Remove-Item -ErrorAction SilentlyContinue $_ }
}

Write-Host "--- $log"
Select-String -Path $log -Pattern 'hit test:|combat: the player (punched|.s throw)' | ForEach-Object { $_.Line }
$failed = 0
if (Test-Path $csv) {
    Write-Host "--- $csv"
    $rows = @(Import-Csv $csv)
    $rows | Format-Table scenario, hit, target, landed, drunk, speed, expected, damage, drop, pass, note -AutoSize | Out-Host
    $failed = @($rows | Where-Object { $_.pass -ne '1' }).Count
    Write-Host "$($rows.Count - $failed) of $($rows.Count) passed" -ForegroundColor $(if ($failed) { 'Red' } else { 'Green' })
}
if ($problem) { Write-Warning $problem }
if (Select-String -Path $log -Pattern 'hit test: stopped' -Quiet) { $failed++ }
if (-not $finished -or $failed) { exit 1 }
