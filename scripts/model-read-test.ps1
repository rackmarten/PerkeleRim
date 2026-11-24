<#
.SYNOPSIS
    How long My Summer Car's frames get while it reads and sends its models to Skyrim, on the test kit (hidden, never
    focused). Makes sure a test session runs (scripts/skytest.ps1 session; one already up for this build is reused),
    then runs scenarios/model-read.json: every model read again with new ids as after MSC starts, sent again as after
    Skyrim restarts, then the van, the moped, the Kekmet and the Gifu called and pictured. Prints MSC's lines on
    each stretch (its frames: the worst, how many over 200 ms) and each model's read, the session
    start's too when this run started it. Results in out/<time>-model-read/, with MSC's log of the run (msc.log) and
    the pictures. A session this script started is stopped at the end (-Keep leaves it up).
    Exit code: 0 passed, both stretches logged and no frame over 200 ms with over 100 ms of reading and sending models
    in it (400 ms with a garbage collection among it); 1 a check failed, a stretch is missing or such a frame; 2 Skyrim
    crashed, exited or hung. Frames over 200 ms
    that were something else in MSC (its own scene load, the machine busy) and gaps over 8 s between chunks are reported.
.EXAMPLE
    scripts/model-read-test.ps1
    scripts/model-read-test.ps1 -Build -Keep
#>
param(
    [switch] $Build,
    [int] $TimeoutSeconds = 1500,  # building the Satsuma whole takes a few minutes
    [switch] $Keep,
    [int] $WaitForLock = 1800  # seconds to wait for another session's games lock
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$skytest = Join-Path $PSScriptRoot 'skytest.ps1'
$file = Join-Path $RepoRoot 'scenarios/model-read.json'
$hitchMs = 200  # FrameWatch.HitchMs
$ownMs = 100    # of a frame over hitchMs, the most the model sender may have taken
$ownCapMs = 400 # and with a garbage collection in it
if ($Build) { & "$PSScriptRoot/build.ps1" }

$json = & $skytest session -Json -WaitForLock $WaitForLock
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$session = $json | ConvertFrom-Json
$ours = -not $session.Reused
Write-Host "$(if ($ours) { 'Started' } else { 'Reusing' }) the test session (Skyrim pid $($session.Pid), $($session.Start))."
$code = 1
$mscLog = Join-Path (Get-RequiredSetting (Get-LocalConfig) 'MscPath') 'output_log.txt'
$pattern = 'PerkeleRim: model: (link-up:|models again|a \d+ ms frame|nothing left to read|[0-9A-F]{8} .* read in)'
try {
    $lines = @(Get-Content $mscLog -ErrorAction SilentlyContinue)
    if ($ours) {
        Write-Host '--- the session start'
        $lines | Select-String -Pattern $pattern | ForEach-Object { $_.Line }
    }
    $from = $lines.Count  # this run's lines only
    $ranAt = Get-Date
    & $skytest run $file -Timeout $TimeoutSeconds
    $code = $LASTEXITCODE
    Write-Host '--- this run'
    $ran = @(Get-Content $mscLog -ErrorAction SilentlyContinue | Select-Object -Skip $from)
    $ran | Select-String -Pattern $pattern | ForEach-Object { $_.Line }
    # The scenario's two stretches, whole: the cold read, then the warm one ("cut short" when the next command came first).
    # A link-up of the session's own start that ends in this run only shows: it holds MSC loading its game.
    $stretches = @($ran | Select-String -Pattern 'model: models again( cold)?: (?!.*cut short).* worst (\d+) ms.* longest gap between chunks (\d+) ms')
    $failed = @()
    if ($stretches.Count -lt 2) { $failed += "MSC logged $($stretches.Count) whole stretches for the scenario's models again, not 2." }
    # MSC logs every frame over 200 ms with how much of it reading and sending models took.
    foreach ($h in ($ran | Select-String -Pattern 'model: a (\d+) ms frame')) {
        $share = [regex]::Match($h.Line, 'model: a \d+ ms frame, .*?(\d+) ms of it reading and sending models')
        $ms = [int]$h.Matches[0].Groups[1].Value
        if (-not $share.Success) { $failed += "A $ms ms frame without its share of model work: an MSC mod older than this script (build it, -Build)."; continue }
        $own = [int]$share.Groups[1].Value
        $gc = [regex]::Match($h.Line, '(\d+) garbage collections in it')
        $collected = $gc.Success -and [int]$gc.Groups[1].Value -gt 0
        # A garbage collection takes 150 to 300 ms of MSC's frame whoever's allocation set it off, and the reads' steps
        # allocate the most: counted and shown, but not the reader's own time.
        if (($own -gt $ownMs -and -not $collected) -or $own -gt $ownCapMs) { $failed += "A $ms ms frame had $own ms of reading and sending models in it." }
        elseif ($own -gt $ownMs) { Write-Host "A $ms ms frame with $own ms of model work, a garbage collection among it." -ForegroundColor Yellow }
        else { Write-Host "A $ms ms frame, but only $own ms of it reading and sending models: something else in MSC$(if ($collected) { ' (a garbage collection)' })." -ForegroundColor Yellow }
    }
    foreach ($s in $stretches) {
        # The session's readiness and the hit test take 8 s without a chunk as MSC done sending (kModelQuiet, skse/src/debug).
        if ([int]$s.Matches[0].Groups[3].Value -gt 8000) { Write-Host 'A gap between chunks was over 8 s: a test could start while MSC still reads.' -ForegroundColor Yellow }
    }
    foreach ($f in ($failed | Select-Object -Unique)) { Write-Host $f -ForegroundColor Yellow }
    if ($failed -and $code -eq 0) { $code = 1 }
    $run = Get-ChildItem (Join-Path $RepoRoot 'out') -Directory -Filter '*-model-read' -ErrorAction SilentlyContinue |
        Where-Object CreationTime -GE $ranAt.AddSeconds(-1) | Sort-Object CreationTime | Select-Object -Last 1
    if ($run -and (Test-Path $mscLog)) { Copy-Item -Force $mscLog (Join-Path $run.FullName 'msc.log') }  # MSC rewrites it at its next start
    if ($run) { Write-Host "Results and pictures: $($run.FullName)" }
} finally {
    if (($ours -and -not $Keep) -or $code -eq 2) { & $skytest stop }
}
exit $code
