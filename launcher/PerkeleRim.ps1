<#
.SYNOPSIS
    PerkeleRim player launcher (ships in the release zip; "Start PerkeleRim.bat" runs it).
    Finds Skyrim SE and My Summer Car (PerkeleRim.ini next to this script, else the Steam
    libraries, else asks once), checks SKSE, Address Library and MSCLoader, installs or updates
    the mod files from files/, adds the starter MSC save from saves/MSC when the player has none,
    starts My Summer Car in a small window that it minimizes once MSC continues its save,
    then starts Skyrim through SKSE.

    -ReportBug ("Report a bug.bat") installs and starts nothing: it zips the PerkeleRim logs,
    PerkeleRim.ini and a versions.txt to "Bug reports" next to this script (the Windows user name replaced by
    %USERPROFILE%) and opens the GitHub bug form with the versions filled in. Nothing is sent
    anywhere; the player attaches the zip. -NoOpen prints the form's address instead of opening it.
#>
param([switch] $NoStart, [switch] $ReportBug, [switch] $NoOpen)

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
    if ($ReportBug) { return $null }  # a bug report works with what it finds, no folder picker
    Write-Host "$name not found in Steam. Pick its install folder (the one with $exe)."
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "$name install folder (the one with $exe)"
    if ($dialog.ShowDialog() -ne 'OK') { Fail "No $name folder picked." }
    if (-not (Test-Path (Join-Path $dialog.SelectedPath $exe))) { Fail "$exe is not in $($dialog.SelectedPath)." }
    return $dialog.SelectedPath
}

# MSCLoader's installer picks where mods go: GF game folder, MD Documents, AD AppData.
function Get-ModsDir($msc) {
    $doorstop = Join-Path $msc 'doorstop_config.ini'
    $setting = if (Test-Path $doorstop) { (Select-String -Path $doorstop -Pattern '^\s*mods\s*=\s*(\w+)' | Select-Object -First 1).Matches.Groups[1].Value }
    switch ($setting) {
        'MD' { Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MySummerCar\Mods' }
        'AD' { Join-Path $env:USERPROFILE 'AppData\LocalLow\MSCLoader\Mods' }
        default { Join-Path $msc 'Mods' }
    }
}

# --- Bug report (-ReportBug) -------------------------------------------------------------------

$IssueUrl = 'https://github.com/rackmarten/PerkeleRim/issues/new'

# Copies a file with the Windows user name taken out of any path in it. Read as Latin-1 so the
# bytes of any encoding survive the round trip; the profile folder is matched as its UTF-8 bytes.
function Copy-Scrubbed($source, $dest) {
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $text = $latin1.GetString([IO.File]::ReadAllBytes($source))
    $profileText = $latin1.GetString([Text.Encoding]::UTF8.GetBytes($env:USERPROFILE))
    $profilePattern = ($profileText -split '[\\/]+' | Where-Object { $_ } | ForEach-Object { [regex]::Escape($_) }) -join '[\\/]+'
    $text = [regex]::Replace($text, "(?i)$profilePattern", '%USERPROFILE%')
    $text = [regex]::Replace($text, '(?i)[A-Z]:[\\/]+Users[\\/]+[^\\/\r\n"''<>|:*?]+', '%USERPROFILE%')
    [IO.File]::WriteAllBytes($dest, $latin1.GetBytes($text))
}

function Version-Of($path, $field) {
    if ($path -and (Test-Path $path)) { (Get-Item $path).VersionInfo.$field }
}

function Send-BugReport($config) {
    $skyrim = Find-Game 'Skyrim Special Edition' 'Skyrim Special Edition' 'SkyrimSE.exe' $config['SkyrimPath']
    $msc = Find-Game 'My Summer Car' 'My Summer Car' 'mysummercar.exe' $config['MscPath']
    $skseDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Skyrim Special Edition\SKSE'
    $modDll = if ($msc) { Join-Path (Get-ModsDir $msc) 'PerkeleRim.dll' }
    $skyrimDll = if ($skyrim) { Join-Path $skyrim 'Data\SKSE\Plugins\PerkeleRim.dll' }

    $skyrimVersion = if ($skyrim) { Version-Of (Join-Path $skyrim 'SkyrimSE.exe') 'FileVersion' }
    $skseVersion = if ($skyrim) { (Version-Of (Join-Path $skyrim 'skse64_loader.exe') 'FileVersion') -replace ',\s*', '.' }
    $loaderVersion = if ($msc) { Version-Of (Join-Path $msc 'mysummercar_Data\Managed\MSCLoader.dll') 'FileVersion' }
    $modVersion = (Version-Of $modDll 'ProductVersion') -replace '\+(\w{7})\w*$', '+$1'
    $build = $null
    if ($msc) {
        $manifest = Join-Path (Split-Path (Split-Path $msc)) 'appmanifest_516750.acf'
        if (Test-Path $manifest) { $build = [regex]::Match((Get-Content -Raw $manifest), '"buildid"\s+"(\d+)"').Groups[1].Value }
    }
    $nt = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $windows = "Windows $(if ([int] $nt.CurrentBuildNumber -ge 22000) { 11 } else { 10 }) $($nt.DisplayVersion), build $($nt.CurrentBuildNumber).$($nt.UBR)"

    $stage = Join-Path ([IO.Path]::GetTempPath()) "PerkeleRim-bug-$([guid]::NewGuid().ToString('N'))"
    $raw = Join-Path $stage 'raw'
    $report = Join-Path $stage 'report'
    New-Item -ItemType Directory -Force $raw, $report | Out-Null
    try {
        $wanted = @(
            @{ Name = 'PerkeleRim-skse.log'; Source = Join-Path $skseDir 'PerkeleRim.log' },
            @{ Name = 'PerkeleRim-msc.log'; Source = if ($msc) { Join-Path $msc 'PerkeleRim_spike.log' } },
            @{ Name = 'output_log.txt'; Source = if ($msc) { Join-Path $msc 'output_log.txt' } },
            @{ Name = 'PerkeleRim.ini'; Source = if ($skyrim) { Join-Path $skyrim 'Data\SKSE\Plugins\PerkeleRim.ini' } })
        $included = @()
        $absent = @()
        foreach ($file in $wanted) {
            if ($file.Source -and (Test-Path $file.Source)) {
                Copy-Item $file.Source (Join-Path $raw $file.Name)
                $included += $file.Name
            } else {
                $where = if ($file.Source) { "looked for $($file.Source)" } else { 'its game folder was not found' }
                $absent += "$($file.Name) ($where)"
            }
        }
        function Or-Unknown($value) { if ($value) { $value } else { 'not found' } }
        function Written($path) { if ($path -and (Test-Path $path)) { (Get-Item $path).LastWriteTime.ToString('s') } else { 'not found' } }
        $included += 'versions.txt'
        $versions = @(
            "SkyrimSE.exe:   $(Or-Unknown $skyrimVersion)",
            "SKSE:           $(Or-Unknown $skseVersion)",
            "MSCLoader:      $(Or-Unknown $loaderVersion)",
            "My Summer Car:  Steam build $(Or-Unknown $build)",
            "PerkeleRim:     $(Or-Unknown $modVersion)",
            "Windows:        $windows",
            '',
            "PerkeleRim.dll in Skyrim last written: $(Written $skyrimDll)",
            "PerkeleRim.dll in MSC last written:    $(Written $modDll)",
            '',
            'Files in this zip:') + @($included | ForEach-Object { " - $_" }) + @('', 'Missing:') +
            $(if ($absent) { @($absent | ForEach-Object { " - $_" }) } else { ' (nothing)' })
        Set-Content -Path (Join-Path $raw 'versions.txt') -Encoding utf8 -Value $versions

        foreach ($name in $included) { Copy-Scrubbed (Join-Path $raw $name) (Join-Path $report $name) }

        $reports = Join-Path $Root 'Bug reports'
        New-Item -ItemType Directory -Force $reports | Out-Null
        $zip = Join-Path $reports "PerkeleRim-bug-$(Get-Date -Format 'yyyy-MM-dd').zip"
        if (Test-Path $zip) { $zip = Join-Path $reports "PerkeleRim-bug-$(Get-Date -Format 'yyyy-MM-dd_HHmmss').zip" }
        Compress-Archive -Path (Join-Path $report '*') -DestinationPath $zip
    } finally {
        Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
    }

    # Only versions go in the address: nothing about the player or the machine.
    $msVersions = if ($build -or $loaderVersion) { "Steam build $build, MSCLoader $loaderVersion" }
    $fields = [ordered] @{ template = 'bug_report.yml'; mod = $modVersion; skyrim = $skyrimVersion; msc = $msVersions }
    $query = ($fields.GetEnumerator() | Where-Object { $_.Value } | ForEach-Object { "$($_.Key)=$([uri]::EscapeDataString($_.Value))" }) -join '&'
    $url = "${IssueUrl}?$query"

    Write-Host ''
    Write-Host "Saved $zip" -ForegroundColor Green
    if ($absent) { Write-Host "Not found (listed in versions.txt): $(($absent | ForEach-Object { ($_ -split ' ')[0] }) -join ', ')" -ForegroundColor Yellow }
    Write-Host 'Nothing is sent anywhere. The zip holds your PerkeleRim logs, PerkeleRim.ini and the game versions.'
    Write-Host ''
    if ($NoOpen) { Write-Host "Bug form: $url"; return }
    Write-Host 'Opening the bug form. Tell us what happened, then drag the zip (shown in a folder window) into the form.'
    Start-Process $url
    Start-Process explorer.exe "/select,`"$zip`""
}

$config = Read-Config
if ($ReportBug) { Send-BugReport $config; exit 0 }
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

$mods = Get-ModsDir $msc

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

# The release zip carries the mod files under files/; a repo checkout takes them from the build output.
$files = Join-Path $Root 'files'
if (Test-Path $files) {
    $sources = @((Join-Path $files 'Skyrim\Data\SKSE\Plugins\PerkeleRim.dll'),
                 (Join-Path $files 'Skyrim\Data\PerkeleRim.esp'),
                 (Join-Path $files 'MSC\Mods\PerkeleRim.dll'))
} else {
    $repo = Split-Path $Root
    $sources = @((Join-Path $repo 'skse\build\release\PerkeleRim.dll'),
                 (Join-Path $repo 'tools\out\esp\PerkeleRim.esp'),
                 (Join-Path $repo 'msc\bin\Release\net35\PerkeleRim.dll'))
}
$missing = @($sources | Where-Object { -not (Test-Path $_) })
if ($missing) { Fail (@('Mod files not found (run scripts/build.ps1 and scripts/build-esp.ps1 first):') + $missing) }
Install-File $sources[0] (Join-Path $skyrim 'Data\SKSE\Plugins')
Install-File $sources[1] (Join-Path $skyrim 'Data')
Install-File $sources[2] $mods

# F11 screenshots go to Screenshots next to this script (ScreenshotDir in the SKSE plugin's ini).
$shotIni = Join-Path $skyrim 'Data\SKSE\Plugins\PerkeleRim.ini'
$shotLine = "ScreenshotDir=$(Join-Path $Root 'Screenshots')"
$iniLines = if (Test-Path $shotIni) { @(Get-Content $shotIni | Where-Object { $_ -notmatch '^\s*ScreenshotDir\s*=' }) } else { @() }
if (-not (Test-Path $shotIni) -or (Get-Content $shotIni) -notcontains $shotLine) {
    [IO.File]::WriteAllLines($shotIni, [string[]](@($iniLines) + $shotLine), (New-Object Text.UTF8Encoding $false))  # no BOM, the plugin reads plain UTF-8
    Write-Host "Screenshots (F11) go to $(Join-Path $Root 'Screenshots')"
}

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
