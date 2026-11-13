<#
.SYNOPSIS
    A test session's deploy and undeploy (skytest.psd1's Deploy and Undeploy; docs/DEV_SETUP.md, "Test sessions").
    "deploy" first keeps a copy of every file scripts/deploy-local.ps1 overwrites (the player's own PerkeleRim, its
    plugins.txt), notes the ones that aren't there, and moves the other mods named after it out of the way (each one's
    SKSE plugin and its .pdb: two mods that both take over the player can't run at once), then runs deploy-local.ps1.
    "restore" puts all of it back as it was and deletes what wasn't there. The copies and their list are kept in
    %LOCALAPPDATA%\PerkeleRim\test-session until a restore; a second deploy before it (a session restarted for a new
    build) keeps the first ones, so the player's own files are what comes back. Run it with both games closed.
.EXAMPLE
    scripts/test-deploy.ps1 deploy AhShitRim   # the build into the games, AhShitRim's plugin aside
    scripts/test-deploy.ps1 restore            # everything as before
    scripts/test-deploy.ps1 status
#>
param(
    [ValidateSet('deploy', 'restore', 'status')] [string] $Mode = 'status',
    # Other mods whose SKSE plugin (<Name>.dll, <Name>.pdb in Data/SKSE/Plugins) stays out of the game meanwhile.
    [Parameter(ValueFromRemainingArguments)] [string[]] $Aside = @()
)

. "$PSScriptRoot/common.ps1"
. "$PSScriptRoot/test-session.ps1"
$config = Get-LocalConfig
$skyrim = Get-RequiredSetting $config 'SkyrimPath'
$mods = Get-RequiredSetting $config 'MscModsPath'
$store = Join-Path $env:LOCALAPPDATA 'PerkeleRim\test-session'

# What deploy-local.ps1 writes (PerkeleRim.ini is the kit's: skytest keeps and restores it itself).
$plugins = Join-Path $skyrim 'Data\SKSE\Plugins'
$ours = @(
    (Join-Path $plugins 'PerkeleRim.dll'),
    (Join-Path $plugins 'PerkeleRim.pdb'),
    (Join-Path $skyrim 'Data\Interface\PerkeleRim\MscMenu.swf'),
    (Join-Path $skyrim 'Data\PerkeleRim.esp'),
    (Join-Path $env:LOCALAPPDATA 'Skyrim Special Edition\plugins.txt'),
    (Join-Path $mods 'PerkeleRim.dll'))
$others = @($Aside | Where-Object { $_ } | ForEach-Object { (Join-Path $plugins "$_.dll"), (Join-Path $plugins "$_.pdb") })

switch ($Mode) {
    'deploy' {
        if (Save-SessionFiles $store -Keep $ours -Move $others) {
            Write-Host "Kept the game's own files in $store$(if ($others) { "; set aside $(@($Aside) -join ', ')" })."
        } else {
            Write-Host "The game's own files from before this session are still kept in $store."
        }
        & "$PSScriptRoot/deploy-local.ps1"
    }
    'restore' {
        $back = Restore-SessionFiles $store
        if ($back) { Write-Host "Put back the game's own files ($($back.Count))." } else { Write-Host 'Nothing kept to put back.' }
    }
    'status' {
        $list = Read-SessionList $store
        if (-not $list) { Write-Host 'No test session files kept.'; return }
        $list | ForEach-Object { [pscustomobject]@{ Path = $_.path; Was = $(if ($_.moved) { 'set aside' } elseif ($_.absent) { 'not there' } else { 'kept' }) } } | Format-Table -AutoSize
    }
}
