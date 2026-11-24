<#
.SYNOPSIS
    The Satsuma moving over to its bay by the garage, on the test kit (hidden, never focused), with each of two My
    Summer Car saves: a fresh game (the starter save: a bare body, every part loose at home) and a half built one
    (body panels, front seats and the dashboard on). For each it starts a test session playing that save
    (PERKELERIM_MSC_SAVE; docs/DEV_SETUP.md, "Test sessions"), runs scenarios/satsuma-garage.json (the move at the
    handover, pictures, a save and a load that moves nothing again) and, after the first, scenarios/garage-board.json
    (-Board, the default), then stops the session, which puts the player's own MSC save back.
    The saves are made from launcher/saves/MSC into %LOCALAPPDATA%\PerkeleRim\msc-saves\satsuma-fresh and
    satsuma-half the first time (the half built one by setting those parts' Installed flags in the save file).
    Exit code: 0 when every run passed, else the first failing run's.
.EXAMPLE
    scripts/satsuma-garage-test.ps1
    scripts/satsuma-garage-test.ps1 -Saves half -Board:$false
#>
param(
    [ValidateSet('fresh', 'half')] [string[]] $Saves = @('fresh', 'half'),
    [bool] $Board = $true,
    [int] $TimeoutSeconds = 300,
    [int] $WaitForLock = 1800  # seconds to wait for another session's games lock
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/common.ps1"
$skytest = Join-Path $PSScriptRoot 'skytest.ps1'
$store = Join-Path $env:LOCALAPPDATA 'PerkeleRim\msc-saves'

# The parts the half built save has on (MSC puts each on when its save loads with Installed set).
$halfBuilt = @('door left(Clone)', 'door right(Clone)', 'fender left(Clone)', 'fender right(Clone)', 'hood(Clone)', 'bootlid(Clone)',
    'bumper front(Clone)', 'bumper rear(Clone)', 'grille(Clone)', 'headlight left(Clone)', 'headlight right(Clone)',
    'seat driver(Clone)', 'seat passenger(Clone)', 'dashboard(Clone)')

# Easy Save 2 keeps a bool as its key (length byte, then the name), 07 00 00 00 ff 9c 7c 4d ad, then the value byte.
function Set-Installed([string] $File, [string[]] $Parts) {
    $bytes = [IO.File]::ReadAllBytes($File)
    $text = [Text.Encoding]::GetEncoding(28591).GetString($bytes)  # one char per byte
    foreach ($part in $Parts) {
        $key = "$part" + 'Installed'
        $marker = [char]0x7B + [char]0x7E + [char]$key.Length + $key + [char]7 + [char]0 + [char]0 + [char]0 + [char]0xFF + [char]0x9C + [char]0x7C + [char]0x4D + [char]0xAD
        $at = $text.IndexOf($marker, [StringComparison]::Ordinal)
        if ($at -lt 0) { throw "No $key in $File." }
        $bytes[$at + $marker.Length] = 1
    }
    [IO.File]::WriteAllBytes($File, $bytes)
}

function Initialize-Save([string] $Name) {
    $folder = Join-Path $store "satsuma-$Name"
    if (Test-Path (Join-Path $folder 'defaultES2File.txt')) { return }
    New-Item -ItemType Directory -Force $folder | Out-Null
    Copy-Item (Join-Path $RepoRoot 'launcher/saves/MSC/*') $folder
    if ($Name -eq 'half') { Set-Installed (Join-Path $folder 'defaultES2File.txt') $halfBuilt }
    Write-Host "Made the $Name MSC save in $folder."
}

$code = 0
$first = $true
foreach ($name in $Saves) {
    Initialize-Save $name
    try { & $skytest stop | Out-Null } catch { }  # ours, up for another save, would be reused; another's is left alone
    $env:PERKELERIM_MSC_SAVE = "satsuma-$name"
    try {
        & $skytest session -WaitForLock $WaitForLock
        if ($LASTEXITCODE) { if (-not $code) { $code = $LASTEXITCODE }; continue }
        Write-Host "== satsuma-garage, $name MSC save" -ForegroundColor Cyan
        & $skytest run (Join-Path $RepoRoot 'scenarios/satsuma-garage.json') -Timeout $TimeoutSeconds
        if ($LASTEXITCODE -and -not $code) { $code = $LASTEXITCODE }
        if ($Board -and $first) {
            Write-Host "== garage-board, $name MSC save" -ForegroundColor Cyan
            & $skytest run (Join-Path $RepoRoot 'scenarios/garage-board.json') -Timeout $TimeoutSeconds
            if ($LASTEXITCODE -and -not $code) { $code = $LASTEXITCODE }
        }
        $first = $false
    } finally {
        & $skytest stop
        Remove-Item Env:PERKELERIM_MSC_SAVE -ErrorAction SilentlyContinue
    }
}
exit $code
