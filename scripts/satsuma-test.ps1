<#
.SYNOPSIS
    The Satsuma drawn in Skyrim as MSC has it built, on the test kit (hidden, never focused): makes sure a test session
    runs (scripts/skytest.ps1 session; one already up for this build is reused), then runs scenarios/satsuma.json (the
    Satsuma brought in front of Kumpo bare, as the starter save has it, then half built through MSC's own assembly) and
    copies MSC's own views of it (PerkeleRim_shot_bare.png, PerkeleRim_shot_half.png) and MSC's log next to Skyrim's
    pictures in out/<time>-satsuma/, as msc-bare.png and msc-half.png beside bare-kumpo.png and half-kumpo.png. A session
    this script started is stopped at the end (-Keep leaves it up); MSC is closed without saving, so the parts put on
    never reach its save. Run it in a fresh session: a session's Satsuma stays half built.
    Exit code: the scenario's (0 passed, 1 a check failed, 2 Skyrim crashed, exited or hung).
.EXAMPLE
    scripts/satsuma-test.ps1
    scripts/satsuma-test.ps1 -Build -Keep
#>
param(
    [switch] $Build,
    [int] $TimeoutSeconds = 300,
    [switch] $Keep,
    [int] $WaitForLock = 1800  # seconds to wait for another session's games lock
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$skytest = Join-Path $PSScriptRoot 'skytest.ps1'
$file = Join-Path $RepoRoot 'scenarios/satsuma.json'
if ($Build) { & "$PSScriptRoot/build.ps1" }

$json = & $skytest session -Json -WaitForLock $WaitForLock
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$session = $json | ConvertFrom-Json
$ours = -not $session.Reused
Write-Host "$(if ($ours) { 'Started' } else { 'Reusing' }) the test session (Skyrim pid $($session.Pid), $($session.Start))."
$code = 1
$msc = Get-RequiredSetting (Get-LocalConfig) 'MscPath'
try {
    $ranAt = Get-Date
    & $skytest run $file -Timeout $TimeoutSeconds
    $code = $LASTEXITCODE
    $run = Get-ChildItem (Join-Path $RepoRoot 'out') -Directory -Filter '*-satsuma' -ErrorAction SilentlyContinue |
        Where-Object CreationTime -GE $ranAt.AddSeconds(-1) | Sort-Object CreationTime | Select-Object -Last 1
    if ($run) {
        foreach ($name in 'bare', 'half') {
            $shot = Join-Path $msc "PerkeleRim_shot_$name.png"
            if ((Test-Path $shot) -and (Get-Item $shot).LastWriteTime -ge $ranAt) { Copy-Item -Force $shot (Join-Path $run.FullName "msc-$name.png") }
            else { Write-Host "MSC saved no view for $name in this run." -ForegroundColor Yellow }
        }
        $log = Join-Path $msc 'output_log.txt'
        if (Test-Path $log) { Copy-Item -Force $log (Join-Path $run.FullName 'msc.log') }
        Get-Content (Join-Path $msc 'output_log.txt') -ErrorAction SilentlyContinue | Select-String 'PerkeleRim: satsuma: ' | Select-Object -Last 30 | ForEach-Object { $_.Line }
        Write-Host "Results and pictures: $($run.FullName)"
    }
} finally {
    if (($ours -and -not $Keep) -or $code -eq 2) { & $skytest stop }
}
exit $code
