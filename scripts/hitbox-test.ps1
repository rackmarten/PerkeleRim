<#
.SYNOPSIS
    The hitbox test on the test kit, hidden and never focused: the van's hitbox with nobody at the keys. Makes sure a
    test session runs (scripts/skytest.ps1 session: the starter save, Kumpo handed to My Summer Car; one already up
    for this build is reused), then runs scenarios/hitbox.json with skytest run: the van called in front of Kumpo, two
    bandit archers' 12 arrows at him (8 through the van, 4 from the side as a control) and a melee bandit behind it
    for a minute (skse/src/vehicle/HitboxTest.cpp), and prints this run's hitbox lines. Results in out/<time>-hitbox/:
    results.csv (its checks), step-<n>-rows.csv (every hitbox test line), summary.json. A session this script started
    is stopped at the end (-Keep leaves it up); one it could reuse is left running. Nothing is saved.
    Exit code: 0 passed, 1 a check failed, 2 Skyrim crashed, exited or hung.
.EXAMPLE
    scripts/hitbox-test.ps1
    scripts/hitbox-test.ps1 -Build -Keep
#>
param(
    [switch] $Build,
    [int] $TimeoutSeconds = 420,
    [switch] $Keep
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$skytest = Join-Path $PSScriptRoot 'skytest.ps1'
if ($Build) { & "$PSScriptRoot/build.ps1" }

$json = & $skytest session -Json
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$session = $json | ConvertFrom-Json
$ours = -not $session.Reused
Write-Host "$(if ($ours) { 'Started' } else { 'Reusing' }) the test session (Skyrim pid $($session.Pid), $($session.Start))."
$code = 1
try {
    $log = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Skyrim Special Edition\SKSE\PerkeleRim.log'
    $from = if (Test-Path $log) { @(Get-Content $log).Count } else { 0 }
    & $skytest run (Join-Path $RepoRoot 'scenarios/hitbox.json') -Timeout $TimeoutSeconds
    $code = $LASTEXITCODE
    Write-Host "--- $log"
    Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Skip $from |
        Select-String -Pattern 'hitbox|player sync: (seated|out of the seat)' | ForEach-Object { $_.Line }
} finally {
    if ($ours -and -not $Keep) { & $skytest stop }
}
exit $code
