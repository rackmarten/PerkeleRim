<#
.SYNOPSIS
    Drives the hidden-MSC spike (msc/src/Spike): sends a command to the running game and shows its log.
    The mod polls <MSC>/PerkeleRim_cmd.txt once per second, runs it and deletes it.
.EXAMPLE
    scripts/spike.ps1 -Command 'dump'                     # FSM index + detail files
    scripts/spike.ps1 -Command 'dump Drink Piss'          # detail only for matching FSMs
    scripts/spike.ps1 -ScriptFile scripts/spike/walk.txt  # scripted input steps
    scripts/spike.ps1                                     # just show the log tail
#>
param(
    [string] $Command,
    [string] $ScriptFile,
    [int] $Wait = 3,
    [int] $Lines = 30
)

. "$PSScriptRoot/common.ps1"
$msc = Get-RequiredSetting (Get-LocalConfig) 'MscPath'

if ($ScriptFile) {
    $body = @('script') + (Get-Content $ScriptFile)
    Set-Content -Path (Join-Path $msc 'PerkeleRim_cmd.txt') -Value $body
    $steps = Get-Content $ScriptFile | Where-Object { $_ -match '^\s*[\d.]+\s*(;|$)' } | ForEach-Object { [double](($_ -split ';')[0]) }
    $Wait = [Math]::Max($Wait, [int][Math]::Ceiling(($steps | Measure-Object -Sum).Sum) + 3)
} elseif ($Command) {
    Set-Content -Path (Join-Path $msc 'PerkeleRim_cmd.txt') -Value $Command
}

if ($Command -or $ScriptFile) { Start-Sleep -Seconds $Wait }
Get-Content -Tail $Lines (Join-Path $msc 'PerkeleRim_spike.log')
