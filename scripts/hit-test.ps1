<#
.SYNOPSIS
    The hit test on the test kit, hidden and never focused: PerkeleRim's attacks on Skyrim's actors with nobody at the
    keys. Makes sure a test session runs (scripts/skytest.ps1 session: the starter save, Kumpo handed to My Summer
    Car, MSC minimized beside it; one already up for this build is reused), then runs scenarios/hits.json with skytest
    run: Kumpo onto open ground by the garage, then protocol/cpp/hit_test.h's scenarios (punch sober, an empty bottle
    thrown, a cabbage thrown as junk, punches drunk, bottles drunk and drunker) at a Whiterun guard placed just ahead
    with its AI off (skse/src/debug/HitTest.cpp), and prints this run's log lines and the rows. Results in
    out/<time>-hits/: results.csv (a row per check), step-<n>-rows.csv (a row per attack: damage cast against
    damage.h, the health it took, pass), summary.json. A session this script started is stopped at the end (-Keep
    leaves it up); one it could reuse is left running. Nothing is saved in either game.
    Exit code: 0 passed, 1 a row or check failed, 2 Skyrim crashed, exited or hung (the crash log is named).
.EXAMPLE
    scripts/hit-test.ps1
    scripts/hit-test.ps1 -Scenarios punch,drunk -Build
    scripts/hit-test.ps1 -Target 0003053F -Keep     # a giant instead (takes two thirds of a hit: its rows fail)
#>
param(
    [string] $Scenarios = 'all',  # protocol/cpp/hit_test.h's, comma separated
    [string] $Target = '',        # an actor base's form id in hex instead of Skyrim.esm's Whiterun guard (000EA0A2)
    [switch] $Build,
    [int] $TimeoutSeconds = 600,
    [switch] $Keep
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$skytest = Join-Path $PSScriptRoot 'skytest.ps1'
$file = Join-Path $RepoRoot 'scenarios/hits.json'
if ($Scenarios -ne 'all' -or $Target) {
    # The same scenario with the pr.hits step's scenarios (and target) changed: written beside the results of earlier runs.
    $text = [IO.File]::ReadAllText($file)
    $with = '"scenarios": "' + ($Scenarios -replace '[^\w,-]', '') + '"' + $(if ($Target) { ', "target": "' + ($Target -replace '[^0-9A-Fa-fx]', '') + '"' })
    $changed = $text.Replace('"scenarios": "all"', $with)
    if ($changed -eq $text) { throw "No pr.hits step with `"scenarios`": `"all`" in $file." }
    New-Item -ItemType Directory -Force (Join-Path $RepoRoot 'out') | Out-Null
    $file = Join-Path $RepoRoot "out/hits-$(($Scenarios -replace '[^\w-]', '+'))$(if ($Target) { "-$Target" }).json"
    [IO.File]::WriteAllText($file, $changed, (New-Object Text.UTF8Encoding $false))
}
if ($Build) { & "$PSScriptRoot/build.ps1" }

# Literal parameters: an array splatted into the forwarder would pass "-Json" on as a plain word.
$json = & $skytest session -Json
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$session = $json | ConvertFrom-Json
$ours = -not $session.Reused
Write-Host "$(if ($ours) { 'Started' } else { 'Reusing' }) the test session (Skyrim pid $($session.Pid), $($session.Start))."
$code = 1
try {
    $log = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Skyrim Special Edition\SKSE\PerkeleRim.log'
    $from = if (Test-Path $log) { @(Get-Content $log).Count } else { 0 }  # this run's lines only
    & $skytest run $file -Timeout $TimeoutSeconds
    $code = $LASTEXITCODE
    Write-Host "--- $log"
    Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Skip $from |
        Select-String -Pattern 'hit test:|combat: the player (punched|.s throw)' | ForEach-Object { $_.Line }
    $run = Get-ChildItem (Join-Path $RepoRoot 'out') -Directory -Filter '*-hits' | Sort-Object LastWriteTime | Select-Object -Last 1
    $rows = if ($run) { Get-ChildItem $run.FullName -Filter 'step-*-rows.csv' | Select-Object -Last 1 }
    if ($rows) {
        Write-Host "--- $($rows.FullName)"
        Import-Csv $rows.FullName | Format-Table scenario, hit, landed, drunk, speed, level, armor, expected, damage, drop, pass, note -AutoSize | Out-Host
    }
} finally {
    if ($ours -and -not $Keep) { & $skytest stop }
}
exit $code
