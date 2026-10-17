<#
.SYNOPSIS
    PerkeleRim player launcher (ships in the release zip; "Start PerkeleRim.bat" runs it).
    Finds Skyrim SE and My Summer Car (PerkeleRim.ini next to this script, else the Steam
    libraries, else asks once), checks SKSE, Address Library and MSCLoader, installs or updates
    the mod files from files/, adds the starter MSC save from saves/MSC when the player has none,
    starts My Summer Car in a small window that it minimizes once MSC continues its save,
    then starts Skyrim through SKSE.
#>
param([switch] $NoStart)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$ConfigPath = Join-Path $Root 'PerkeleRim.ini'

function Fail($lines) {
    Write-Host ''
    foreach ($line in @($lines)) { Write-Host $line -ForegroundColor Red }
    exit 1
}

# --- Game folders ---------------------------------------------------------------------------

function Read-Config {
    $config = @{}
    if (Test-Path $ConfigPath) {
        foreach ($line in Get-Content $ConfigPath) {
            if ($line -match '^\s*(\w+)\s*=\s*(.*?)\s*$') { $config[$Matches[1]] = $Matches[2] }
        }
    }
    return $config
}

function Get-SteamLibraries {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { return @() }
    $libraries = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        foreach ($match in [regex]::Matches((Get-Content -Raw $vdf), '"path"\s+"([^"]+)"')) {
            $libraries += $match.Groups[1].Value -replace '\\\\', '\'
        }
    }
    return $libraries | Select-Object -Unique
}

function Find-Game($name, $folder, $exe, $configured) {
    if ($configured -and (Test-Path (Join-Path $configured $exe))) { return $configured }
    foreach ($library in Get-SteamLibraries) {
        $path = Join-Path $library "steamapps\common\$folder"
        if (Test-Path (Join-Path $path $exe)) { return (Resolve-Path $path).Path }
    }
    Write-Host "$name not found in Steam. Pick its install folder (the one with $exe)."
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "$name install folder (the one with $exe)"
    if ($dialog.ShowDialog() -ne 'OK') { Fail "No $name folder picked." }
    if (-not (Test-Path (Join-Path $dialog.SelectedPath $exe))) { Fail "$exe is not in $($dialog.SelectedPath)." }
    return $dialog.SelectedPath
}

$config = Read-Config
$skyrim = Find-Game 'Skyrim Special Edition' 'Skyrim Special Edition' 'SkyrimSE.exe' $config['SkyrimPath']
$msc = Find-Game 'My Summer Car' 'My Summer Car' 'mysummercar.exe' $config['MscPath']
if ($skyrim -ne $config['SkyrimPath'] -or $msc -ne $config['MscPath']) {
    Set-Content -Path $ConfigPath -Encoding utf8 -Value @(
        '; Game folders for the PerkeleRim launcher. Delete this file to look them up again.',
        "SkyrimPath=$skyrim",
        "MscPath=$msc")
}
Write-Host "Skyrim:        $skyrim"
Write-Host "My Summer Car: $msc"

# --- Requirements ---------------------------------------------------------------------------

$missing = @()
if (-not (Test-Path (Join-Path $skyrim 'skse64_loader.exe'))) {
    $missing += 'SKSE64 is not installed in Skyrim. Get the build for your Skyrim version from https://skse.silverlock.org and copy skse64_loader.exe and its DLLs into the Skyrim folder.'
}
$version = (Get-Item (Join-Path $skyrim 'SkyrimSE.exe')).VersionInfo.FileVersion
if ($version -match '^(\d+)\.(\d+)\.(\d+)') {
    $library = "versionlib-$($Matches[1])-$($Matches[2])-$($Matches[3])-0.bin"
    if (-not (Test-Path (Join-Path $skyrim "Data\SKSE\Plugins\$library"))) {
        $missing += "Address Library for SKSE Plugins is not installed for Skyrim $version ($library). Get the All in one (Anniversary Edition) file from https://www.nexusmods.com/skyrimspecialedition/mods/32444 and put it in Data\SKSE\Plugins."
    }
}
$doorstop = Join-Path $msc 'doorstop_config.ini'
if (-not (Test-Path $doorstop) -or -not (Test-Path (Join-Path $msc 'mysummercar_Data\Managed\MSCLoader.dll'))) {
    $missing += 'MSCLoader is not installed in My Summer Car. Get it from https://www.nexusmods.com/mysummercar/mods/147 and run its installer.'
}
if ($missing) { Fail (@('Missing:') + ($missing | ForEach-Object { " - $_" }) + '', 'Install them and run the launcher again.') }

# MSCLoader's installer picks where mods go: GF game folder, MD Documents, AD AppData.
$modsSetting = (Select-String -Path $doorstop -Pattern '^\s*mods\s*=\s*(\w+)' | Select-Object -First 1).Matches.Groups[1].Value
$mods = switch ($modsSetting) {
    'MD' { Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MySummerCar\Mods' }
    'AD' { Join-Path $env:USERPROFILE 'AppData\LocalLow\MSCLoader\Mods' }
    default { Join-Path $msc 'Mods' }
}

$skyrimRunning = Get-Process SkyrimSE, skse64_loader -ErrorAction SilentlyContinue
$mscRunning = Get-Process mysummercar -ErrorAction SilentlyContinue
if ($skyrimRunning) { Fail 'Skyrim is already running. Close it and run the launcher again.' }

# --- Install --------------------------------------------------------------------------------

function Install-File($source, $destDir) {
    $dest = Join-Path $destDir (Split-Path -Leaf $source)
    if ((Test-Path $dest) -and (Get-FileHash $dest).Hash -eq (Get-FileHash $source).Hash) { return }
    New-Item -ItemType Directory -Force $destDir | Out-Null
    try { Copy-Item -Force $source $dest }
    catch { Fail "Can't write $dest ($($_.Exception.Message)). Close both games and run the launcher again." }
    Write-Host "Installed $dest"
}

$files = Join-Path $Root 'files'
Install-File (Join-Path $files 'Skyrim\Data\SKSE\Plugins\PerkeleRim.dll') (Join-Path $skyrim 'Data\SKSE\Plugins')
Install-File (Join-Path $files 'Skyrim\Data\PerkeleRim.esp') (Join-Path $skyrim 'Data')
Install-File (Join-Path $files 'MSC\Mods\PerkeleRim.dll') $mods

# Skyrim SE lists active plugins as "*Name.esp", in load order, after the official masters.
$plugins = Join-Path $env:LOCALAPPDATA 'Skyrim Special Edition\plugins.txt'
$lines = if (Test-Path $plugins) { @(Get-Content $plugins) } else { @() }
if ($lines -notcontains '*PerkeleRim.esp') {
    New-Item -ItemType Directory -Force (Split-Path $plugins) | Out-Null
    Set-Content -Path $plugins -Encoding ascii -Value (@($lines | Where-Object { $_ -ne 'PerkeleRim.esp' }) + '*PerkeleRim.esp')
    Write-Host "Enabled PerkeleRim.esp in $plugins"
}

# The starter MSC save, never over the player's own: MSC has one save slot, so only when it has none.
$mscSaves = Join-Path $env:USERPROFILE 'AppData\LocalLow\Amistech\My Summer Car'
$starterMsc = Join-Path $Root 'saves\MSC'
if ((Test-Path $starterMsc) -and -not (Test-Path (Join-Path $mscSaves 'defaultES2File.txt'))) {
    New-Item -ItemType Directory -Force $mscSaves | Out-Null
    Copy-Item -Force (Join-Path $starterMsc '*') $mscSaves
    Write-Host 'Added the starter My Summer Car save.'
}
if (-not (Test-Path (Join-Path $mscSaves 'defaultES2File.txt'))) {
    Write-Host 'My Summer Car has no save yet: start a new game in it once (restore its window), then press F10 in Skyrim.' -ForegroundColor Yellow
}

# The mod continues MSC's save by itself on its main menu while this file exists.
Set-Content -Path (Join-Path $msc 'PerkeleRim_autoload.txt') -Value 'Written by the PerkeleRim launcher.'

if ($NoStart) { Write-Host 'Installed, not starting the games (-NoStart).'; return }

# --- Start My Summer Car ----------------------------------------------------------------------

Add-Type -Namespace PerkeleRimLauncher -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
[DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
'@
$win32 = [PerkeleRimLauncher.Win32]

if ($mscRunning) {
    Write-Host 'My Summer Car is already running, leaving it as it is.'
} else {
    Write-Host 'Starting My Summer Car (it minimizes itself once its menu is up)'
    # The mod's log is rewritten when MSCLoader loads it on the main menu.
    $log = Join-Path $msc 'PerkeleRim_spike.log'
    $launched = Get-Date
    Start-Process -WorkingDirectory $msc (Join-Path $msc 'mysummercar.exe') `
        -ArgumentList '-force-gfx-direct', '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '480', '-screen-quality', 'Fastest'

    # Until the mod has started: click "Play!" on Unity's configuration dialog, and press Space on
    # the game window for the "press any key" news screen (a real key press: with
    # -force-gfx-direct, Unity ignores posted key messages). Same as scripts/launch-msc.ps1.
    $clicked = $false
    $started = $false
    $deadline = $launched.AddMinutes(4)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
        if ((Test-Path $log) -and (Get-Item $log).LastWriteTime -gt $launched) { $started = $true; break }
        if (-not $clicked) {
            $dialog = $win32::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, '#32770', 'My Summer Car Configuration')
            $owner = 0
            if ($dialog -ne [IntPtr]::Zero) { $win32::GetWindowThreadProcessId($dialog, [ref] $owner) | Out-Null }
            # Steam may restart the game under a new pid, so any mysummercar process counts.
            $play = if ($owner -and (Get-Process mysummercar -ErrorAction SilentlyContinue | Where-Object Id -eq $owner)) { $win32::FindWindowEx($dialog, [IntPtr]::Zero, 'Button', 'Play!') } else { [IntPtr]::Zero }
            if ($play -ne [IntPtr]::Zero) {
                $win32::SendMessage($play, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK
                $clicked = $true
            }
        }
        $game = $win32::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, 'UnityWndClass', 'My Summer Car')
        if ($game -ne [IntPtr]::Zero -and $win32::IsWindowVisible($game)) {
            $win32::SetForegroundWindow($game) | Out-Null
            Start-Sleep -Milliseconds 200
            $win32::keybd_event(0x20, 0x39, 0, [UIntPtr]::Zero)  # Space down
            Start-Sleep -Milliseconds 100
            $win32::keybd_event(0x20, 0x39, 2, [UIntPtr]::Zero)  # KEYEVENTF_KEYUP
            Start-Sleep -Milliseconds 1400
        }
    }
    if (-not $started) {
        Fail @('My Summer Car did not reach its main menu with the mod loaded within 4 minutes.',
            "Check that MSCLoader loads mods from $mods and that PerkeleRim shows in its mod list.")
    }
    $game = $win32::FindWindowEx([IntPtr]::Zero, [IntPtr]::Zero, 'UnityWndClass', 'My Summer Car')
    if ($game -ne [IntPtr]::Zero) { $win32::ShowWindow($game, 6) | Out-Null }  # SW_MINIMIZE
    Write-Host 'My Summer Car is up and minimized; it continues its save by itself.'
}

# --- Start Skyrim ---------------------------------------------------------------------------

Write-Host 'Starting Skyrim (SKSE)'
Start-Process -WorkingDirectory $skyrim (Join-Path $skyrim 'skse64_loader.exe')
Write-Host ''
Write-Host 'Load a save in Skyrim (or start a new game) and press F10 to hand over to My Summer Car.' -ForegroundColor Green
