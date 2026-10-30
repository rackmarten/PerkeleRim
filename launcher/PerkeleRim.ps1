<#
.SYNOPSIS
    PerkeleRim player launcher (ships in the release zip; "Start PerkeleRim.bat" runs it).
    The first time (no usable game folders in PerkeleRim.ini next to this script), setup finds
    My Summer Car and Skyrim SE (Steam libraries, or the player picks them) and waits while the
    player installs what's missing of MSCLoader, SKSE and Address Library (wizard.ps1). Every
    time, it checks those (checks.ps1), installs or updates the mod files from files/, enables
    PerkeleRim.esp, adds the starter MSC save from saves/MSC when the player has none and the
    starter Skyrim save from saves/Skyrim when it isn't there yet (install.ps1), starts My Summer
    Car in a small window that it minimizes once MSC continues its save, then starts Skyrim:
    through Mod Organizer 2 when the player said so (ModManager=MO2), else through SKSE.
    MSC's own screen settings are put back once it closes, by a hidden copy of this script
    started with -RestoreMscScreen.

    -NoStart checks and installs, and starts nothing.
    -ReportBug ("Report a bug.bat") installs and starts nothing: it zips the PerkeleRim logs,
    PerkeleRim.ini and a versions.txt to "Bug reports" next to this script (the Windows user name replaced by
    %USERPROFILE%) and opens the GitHub bug form with the versions filled in. Nothing is sent
    anywhere; the player attaches the zip. -NoOpen prints the form's address instead of opening it.
#>
param([switch] $NoStart, [switch] $ReportBug, [switch] $NoOpen, [switch] $RestoreMscScreen)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$ConfigPath = Join-Path $Root 'PerkeleRim.ini'
. (Join-Path $Root 'checks.ps1')
. (Join-Path $Root 'install.ps1')

# The hidden watcher: waits for My Summer Car to close, then puts its screen settings back.
if ($RestoreMscScreen) {
    Wait-MscExit
    [void] (Restore-MscScreenSettings $MscRegistryKey $MscScreenBackup)
    exit 0
}

. (Join-Path $Root 'wizard.ps1')

function Fail($lines) {
    Write-Host ''
    foreach ($line in @($lines)) { Write-Host $line -ForegroundColor Red }
    exit 1
}

# --- Settings -------------------------------------------------------------------------------

function Read-Config {
    $config = @{}
    if (Test-Path $ConfigPath) {
        foreach ($line in Get-Content $ConfigPath) {
            if ($line -match '^\s*(\w+)\s*=\s*(.*?)\s*$') { $config[$Matches[1]] = $Matches[2] }
        }
    }
    return $config
}

function Save-Config {
    $lines = @('; Settings for the PerkeleRim launcher. Delete this file to run setup again.')
    foreach ($key in 'SkyrimPath', 'MscPath', 'ModManager', 'Mo2Path', 'Mo2Exe', 'HelgenSide') { if ($config[$key]) { $lines += "$key=$($config[$key])" } }
    Set-Content -Path $ConfigPath -Encoding utf8 -Value $lines
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
    # Works with what it finds, no setup and no folder picker.
    $libraries = Get-SteamLibraries
    $skyrim = Find-GameFolder $Games.Skyrim $config['SkyrimPath'] $libraries
    $msc = Find-GameFolder $Games.Msc $config['MscPath'] $libraries
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

$mscRunning = Get-Process mysummercar -ErrorAction SilentlyContinue
# A restore that never ran (the PC was shut down while MSC ran) happens now.
if (-not $mscRunning -and (Restore-MscScreenSettings $MscRegistryKey $MscScreenBackup)) { Write-Host "Put My Summer Car's own screen settings back." }

# --- Game folders ---------------------------------------------------------------------------

# Setup runs while PerkeleRim.ini doesn't name both games' folders (the first time, or a game
# that moved). A folder still there is kept, and the folders are saved as soon as both are known.
$known = @{}
foreach ($key in 'Skyrim', 'Msc') {
    $folder = $config["$($key)Path"]
    $exe = Join-Guess $folder $Games[$key].Exe
    if ($exe -and (Test-Path -LiteralPath $exe)) { $known[$key] = $folder }
}
if ($known.Count -lt 2) {
    $found = Invoke-PerkeleRimSetup (Get-SteamLibraries) $known
    if (-not $found) { Fail "Setup stopped before both games were found. Run Start PerkeleRim.bat again to carry on. Help: $($Links.Install)" }
    $config['SkyrimPath'] = $found.Skyrim
    $config['MscPath'] = $found.Msc
    Save-Config
}
$skyrim = $config['SkyrimPath']
$msc = $config['MscPath']
Write-Host "Skyrim:        $skyrim"
Write-Host "My Summer Car: $msc"

# --- Mod Organizer 2 ------------------------------------------------------------------------

# Asked once when an MO2 instance manages this Skyrim; ModManager in PerkeleRim.ini keeps it.
$instances = @(Get-Mo2Instances $skyrim (Get-Mo2InstanceDirs $config['Mo2Path']))
if (-not $config['ModManager'] -and $instances) {
    $config['ModManager'] = Select-Mo2Use $instances[0].Name
    Save-Config
}
$mo2 = $null
if ($config['ModManager'] -eq 'MO2') {
    $mo2 = $instances | Select-Object -First 1
    if (-not $mo2) { Write-Host 'PerkeleRim.ini says ModManager=MO2, but no Mod Organizer 2 instance manages this Skyrim (a portable one: set Mo2Path to its folder). Starting Skyrim through SKSE.' -ForegroundColor Yellow }
}

# --- Requirements ---------------------------------------------------------------------------

$skyrimExe = Join-Path $skyrim 'SkyrimSE.exe'
$getProblems = {
    # Read again on every Retry: Address Library may have just gone in as an MO2 mod.
    if ($mo2) { $script:mo2 = @(Get-Mo2Instances $skyrim (Get-Mo2InstanceDirs $config['Mo2Path']) | Where-Object { Test-SamePath $_.Dir $mo2.Dir }) + $mo2 | Select-Object -First 1 }
    $pluginFiles = @(Get-FileNames (Join-Path $skyrim 'Data\SKSE\Plugins')) + @(if ($mo2) { $mo2.PluginFiles })
    @(Get-MscProblems (Get-FileNames $msc) (Get-FileNames (Join-Path $msc 'mysummercar_Data\Managed'))) +
        @(Get-SkyrimProblems (Get-Item -LiteralPath $skyrimExe).VersionInfo.FileVersion (Get-FileNames $skyrim) $pluginFiles)
}
if (-not (Wait-Requirements $getProblems)) {
    Fail (@('Not ready yet:') + (@(& $getProblems) | ForEach-Object { " - $_" }) + '', "Install them and run the launcher again. Help: $($Links.Install)")
}
$runtime = Get-SkyrimRuntime (Get-Item -LiteralPath $skyrimExe).VersionInfo.FileVersion
$se = -not $runtime.IsAnniversary
Write-Host "Checks passed: Skyrim $($runtime.Version) with SKSE and Address Library, My Summer Car with MSCLoader."
if ($mo2) { foreach ($warning in Get-Mo2Warnings $mo2.Name $mo2.PerkeleRimMods $mo2.SkseTitle) { Write-Host $warning -ForegroundColor Yellow } }

$mods = Get-ModsDir $msc
if (Get-Process SkyrimSE, skse64_loader -ErrorAction SilentlyContinue) { Fail 'Skyrim is already running. Close it and run the launcher again.' }

# --- Install --------------------------------------------------------------------------------

# The release zip carries the mod files under files/; a repo checkout takes them from the build output.
$files = Join-Path $Root 'files'
if (Test-Path $files) {
    $sources = @((Join-Path $files 'Skyrim\Data\SKSE\Plugins\PerkeleRim.dll'),
                 (Join-Path $files 'Skyrim\Data\Interface\PerkeleRim\MscMenu.swf'),
                 (Join-Path $files 'Skyrim\Data\PerkeleRim.esp'),
                 (Join-Path $files 'MSC\Mods\PerkeleRim.dll'))
} else {
    $repo = Split-Path $Root
    $sources = @((Join-Path $repo 'skse\build\release\PerkeleRim.dll'),
                 (Join-Path $repo 'skse\build\release\MscMenu.swf'),
                 (Join-Path $repo 'tools\out\esp\PerkeleRim.esp'),
                 (Join-Path $repo 'msc\bin\Release\net35\PerkeleRim.dll'))
}
$missing = @($sources | Where-Object { -not (Test-Path $_) })
if ($missing) { Fail (@('Mod files not found (run scripts/build.ps1 and scripts/build-esp.ps1 first):') + $missing) }
$targets = @((Join-Path $skyrim 'Data\SKSE\Plugins'), (Join-Path $skyrim 'Data\Interface\PerkeleRim'), (Join-Path $skyrim 'Data'), $mods)
for ($i = 0; $i -lt $sources.Count; $i++) {
    try { if (Install-ModFile $sources[$i] $targets[$i]) { Write-Host "Installed $(Join-Path $targets[$i] (Split-Path -Leaf $sources[$i]))" } }
    catch { Fail "Can't write $(Join-Path $targets[$i] (Split-Path -Leaf $sources[$i])) ($($_.Exception.Message)). Close both games and run the launcher again." }
}

# F11 screenshots go to Screenshots next to this script (ScreenshotDir in the SKSE plugin's ini).
$shots = Join-Path $Root 'Screenshots'
if (Set-ScreenshotDir (Join-Path $skyrim 'Data\SKSE\Plugins\PerkeleRim.ini') $shots) { Write-Host "Screenshots (F11) go to $shots" }

# Who a new game's skipped Helgen sends Kumpo to (HelgenSide=Stormcloak for Ralof, Imperial or
# nothing for Hadvar). Copied to the plugin's ini only when set here: without it the plugin's ini
# keeps whatever it has.
if (Set-PluginSetting (Join-Path $skyrim 'Data\SKSE\Plugins\PerkeleRim.ini') 'HelgenSide' $config['HelgenSide']) {
    Write-Host "New games skip Helgen on the $($config['HelgenSide']) side"
}

# Skyrim lists active plugins in plugins.txt: its own in %LOCALAPPDATA%, or with MO2 the selected
# profile's, which MO2 rewrites while it's open.
if ($mo2) {
    $plugins = Join-Path $mo2.ProfileDir 'plugins.txt'
    $enabled = (Test-Path -LiteralPath $plugins) -and @(Get-Content -LiteralPath $plugins) -contains '*PerkeleRim.esp'
    if (-not $enabled -and (Get-Process ModOrganizer -ErrorAction SilentlyContinue)) {
        Write-Host "Mod Organizer 2 is open, so tick PerkeleRim.esp in its right pane (Plugins) yourself." -ForegroundColor Yellow
    } elseif (Enable-SkyrimPlugin $plugins 'PerkeleRim.esp') { Write-Host "Enabled PerkeleRim.esp in $plugins" }
} else {
    $plugins = Join-Path $env:LOCALAPPDATA 'Skyrim Special Edition\plugins.txt'
    if (Enable-SkyrimPlugin $plugins 'PerkeleRim.esp') { Write-Host "Enabled PerkeleRim.esp in $plugins" }
}

# The starter MSC save, never over the player's own: MSC has one save slot, so only when it has none.
$mscSaves = Join-Path $env:USERPROFILE 'AppData\LocalLow\Amistech\My Summer Car'
if (Add-StarterSave (Join-Path $Root 'saves\MSC') $mscSaves 'defaultES2File.txt') { Write-Host 'Added the starter My Summer Car save.' }
if (-not (Test-Path (Join-Path $mscSaves 'defaultES2File.txt'))) {
    Write-Host 'My Summer Car has no save yet: start a new game in it once (restore its window), then load a save in Skyrim.' -ForegroundColor Yellow
}

# The starter Skyrim save (Kumpo, never handed over yet) next to the player's own, never over one.
# It was made on Anniversary Edition and Special Edition 1.5.x calls it corrupt, so SE gets none.
$skyrimSaves = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'My Games\Skyrim Special Edition\Saves'
if ($mo2 -and $mo2.LocalSaves) {
    Write-Host "No starter Skyrim save: MO2's profile keeps its own saves. Start a new game or load your own."
} elseif ($se) {
    Write-Host "No starter Skyrim save on Skyrim $($runtime.Version) (it only loads on Anniversary Edition): start a new game or load your own."
} elseif (Add-StarterSave (Join-Path $Root 'saves\Skyrim') $skyrimSaves 'PerkeleRim-Kumpo.ess') {
    Write-Host 'Added the starter Skyrim save (Kumpo): pick it under Load.'
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

# Setup may have taken a while, and the player may have started MSC from Steam meanwhile.
$mscRunning = Get-Process mysummercar -ErrorAction SilentlyContinue
if ($mscRunning) {
    Write-Host 'My Summer Car is already running, leaving it as it is.'
    # Its screen settings from before still wait to go back: make sure something waits for it.
    if (Test-Path -LiteralPath $MscScreenBackup) { Start-MscScreenWatcher $PSCommandPath }
} else {
    Write-Host 'Starting My Summer Car (it minimizes itself once its menu is up)'
    # MSC runs in a small window at its lowest quality, and Unity keeps that as MSC's own screen
    # settings when it quits: the player's are saved now and put back once MSC has closed.
    [void] (Save-MscScreenSettings $MscRegistryKey $MscScreenBackup)
    # The mod's log is rewritten when MSCLoader loads it on the main menu.
    $log = Join-Path $msc 'PerkeleRim_spike.log'
    $launched = Get-Date
    Start-Process -WorkingDirectory $msc (Join-Path $msc 'mysummercar.exe') `
        -ArgumentList '-force-gfx-direct', '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '480', '-screen-quality', 'Fastest'
    Start-MscScreenWatcher $PSCommandPath

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

# ModOrganizer.exe: Mo2Exe in PerkeleRim.ini, a portable instance's own folder, a running MO2, the
# nxm:// link handler MO2 registers, or else asked for once (and kept as Mo2Exe).
function Find-Mo2Exe($instance) {
    $candidates = @($config['Mo2Exe'])
    if ($instance.ShortcutName -eq '') { $candidates += Join-Path $instance.Dir 'ModOrganizer.exe' }
    $candidates += @(Get-Process ModOrganizer -ErrorAction SilentlyContinue | ForEach-Object Path)
    $nxm = (Get-ItemProperty 'HKCU:\Software\Classes\nxm\shell\open\command' -ErrorAction SilentlyContinue).'(default)'
    if ($nxm) { $candidates += Get-Mo2ExeFromCommand $nxm }
    foreach ($candidate in $candidates) { if ($candidate -and (Test-Path $candidate)) { return $candidate } }
    Write-Host 'Pick ModOrganizer.exe, to start Skyrim through Mod Organizer 2.'
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = 'ModOrganizer.exe'
    $dialog.Filter = 'ModOrganizer.exe|ModOrganizer.exe'
    if ($dialog.ShowDialog() -ne 'OK') { return $null }
    $config['Mo2Exe'] = $dialog.FileName
    Save-Config
    return $dialog.FileName
}

if ($mo2) {
    # MO2 runs the shortcut's executable itself, or hands it to the MO2 already open (which
    # refuses, with a message, when that one is open for another instance).
    $mo2Exe = if ($mo2.SkseTitle) { Find-Mo2Exe $mo2 }
    if (-not $mo2Exe) {
        Write-Host ''
        Write-Host 'Start Skyrim from Mod Organizer 2 now: pick SKSE in its executables list and press Run.' -ForegroundColor Green
        Write-Host 'Then load a save: Kumpo goes over to My Summer Car by himself.' -ForegroundColor Green
        Read-Host 'Press Enter to close this window' | Out-Null
        return
    }
    Write-Host "Starting Skyrim through Mod Organizer 2 ('$($mo2.SkseTitle)' in '$($mo2.Name)')"
    Start-Process -FilePath $mo2Exe -WorkingDirectory (Split-Path $mo2Exe) -ArgumentList "`"$(Get-Mo2ShortcutUri $mo2.ShortcutName $mo2.SkseTitle)`""
} else {
    Write-Host 'Starting Skyrim (SKSE)'
    Start-Process -WorkingDirectory $skyrim (Join-Path $skyrim 'skse64_loader.exe')
}
Write-Host ''
Write-Host 'Load a save in Skyrim (or start a new game): Kumpo goes over to My Summer Car by himself.' -ForegroundColor Green
