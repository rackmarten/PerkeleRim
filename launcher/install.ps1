<#
.SYNOPSIS
    What the launcher installs, as functions with no dialogs: finding both games (Steam libraries,
    Steam's uninstall entries), copying the mod files, enabling PerkeleRim.esp, the starter saves,
    Mod Organizer 2 instances, and keeping My Summer Car's own screen settings around a PerkeleRim
    session. launcher/tests/install.Tests.ps1 covers them on temporary folders and a temporary
    registry key. wizard.ps1 asks the player; PerkeleRim.ps1 dot-sources checks.ps1 first, then both.
#>

$Games = @{
    Skyrim = [pscustomobject] @{ Name = 'Skyrim Special Edition'; SteamFolder = 'Skyrim Special Edition'; Exe = 'SkyrimSE.exe'; AppId = 489830 }
    Msc    = [pscustomobject] @{ Name = 'My Summer Car'; SteamFolder = 'My Summer Car'; Exe = 'mysummercar.exe'; AppId = 516750 }
}

# --- Game folders -------------------------------------------------------------------------------

# $Base\$Child as plain text, or $null when it can't be one. Join-Path and Split-Path throw for a
# drive that doesn't exist (a Steam library on a drive since removed), so guesses use these instead.
function Join-Guess([string] $Base, [string] $Child) {
    if (-not $Base) { return $null }
    try { return [IO.Path]::Combine($Base, $Child) } catch { return $null }
}

# Steam's library folders: Steam's own folder and every "path" in its libraryfolders.vdf, which
# may name a drive that isn't there any more. $SteamPath defaults to the one in the registry.
function Get-SteamLibraries([string] $SteamPath = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath) {
    if (-not $SteamPath) { return @() }
    $libraries = @($SteamPath -replace '/', '\')
    $vdf = Join-Guess $SteamPath 'steamapps\libraryfolders.vdf'
    if ($vdf -and (Test-Path -LiteralPath $vdf)) {
        foreach ($match in [regex]::Matches((Get-Content -Raw -LiteralPath $vdf), '"path"\s+"([^"]+)"')) {
            $libraries += $match.Groups[1].Value -replace '\\\\', '\'
        }
    }
    # Once each: the registry often has Steam's folder in lower case, the .vdf as it is on disk.
    $seen = @{}
    return @($libraries | Where-Object { $key = $_.TrimEnd('\').ToLowerInvariant(); if ($seen[$key]) { $false } else { $seen[$key] = $true; $true } })
}

# A game folder from a registry value: a folder, or an exe path (quoted or not).
function Get-FolderFromRegistryValue([string] $Value) {
    if (-not $Value) { return $null }
    $path = $Value.Trim().Trim('"')
    try { if ($path -match '\.exe$') { $path = [IO.Path]::GetDirectoryName($path) } } catch { return $null }
    if (-not $path) { return $null }
    return $path.TrimEnd('\')
}

# The folders where $Game (a $Games entry) may be: each Steam library's steamapps\common, and
# Steam's uninstall entry for it ("Steam App <id>"), which knows a library Steam's own list lost.
# Not checked on disk, and never throws: a folder on a drive that's gone is just a guess
# Select-GameInstalls drops.
function Get-GameFolderGuesses($Game, $SteamLibraries) {
    $guesses = @()
    foreach ($library in @($SteamLibraries)) { $guesses += Join-Guess $library "steamapps\common\$($Game.SteamFolder)" }
    foreach ($hive in 'HKLM:\Software\WOW6432Node', 'HKLM:\Software', 'HKCU:\Software') {
        $entry = Get-ItemProperty "$hive\Microsoft\Windows\CurrentVersion\Uninstall\Steam App $($Game.AppId)" -ErrorAction SilentlyContinue
        if ($entry) { $guesses += Get-FolderFromRegistryValue $entry.InstallLocation }
    }
    return @($guesses | Where-Object { $_ })
}

# The guesses that hold $Exe, once each, as full paths. A folder that can't be read (a drive that's
# gone, a bad name) is skipped.
function Select-GameInstalls([string[]] $Folders, [string] $Exe) {
    $seen = @{}
    foreach ($folder in $Folders) {
        try {
            if (-not $folder -or -not (Test-Path -LiteralPath $folder -PathType Container)) { continue }
            $full = (Resolve-Path -LiteralPath $folder).Path.TrimEnd('\')
            if ($seen[$full.ToLowerInvariant()]) { continue }
            $seen[$full.ToLowerInvariant()] = $true
            if (Test-Path -LiteralPath ([IO.Path]::Combine($full, $Exe))) { $full }
        } catch { continue }
    }
}

# $Configured when it still has the game's exe, else the first install found, else $null.
function Find-GameFolder($Game, [string] $Configured, $SteamLibraries) {
    $configuredExe = Join-Guess $Configured $Game.Exe
    if ($configuredExe -and (Test-Path -LiteralPath $configuredExe)) { return $Configured }
    return Select-GameInstalls (Get-GameFolderGuesses $Game $SteamLibraries) $Game.Exe | Select-Object -First 1
}

function Get-FileNames([string] $Dir) {
    if ($Dir -and (Test-Path -LiteralPath $Dir)) { @(Get-ChildItem -LiteralPath $Dir -File | ForEach-Object Name) } else { @() }
}

# Where MSCLoader keeps mods (doorstop_config.ini's setting, Get-ModsDirSetting in checks.ps1).
function Get-ModsDir([string] $MscPath) {
    $doorstop = Join-Path $MscPath 'doorstop_config.ini'
    $text = if (Test-Path -LiteralPath $doorstop) { Get-Content -Raw -LiteralPath $doorstop } else { '' }
    switch (Get-ModsDirSetting $text) {
        'MD' { Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MySummerCar\Mods' }
        'AD' { Join-Path $env:USERPROFILE 'AppData\LocalLow\MSCLoader\Mods' }
        default { Join-Path $MscPath 'Mods' }
    }
}

# --- Mod files ----------------------------------------------------------------------------------

# Copies $Source into $DestDir unless an identical file is there. $true when it copied.
function Install-ModFile([string] $Source, [string] $DestDir) {
    $dest = Join-Path $DestDir (Split-Path -Leaf $Source)
    if ((Test-Path -LiteralPath $dest) -and (Get-FileHash -LiteralPath $dest).Hash -eq (Get-FileHash -LiteralPath $Source).Hash) { return $false }
    New-Item -ItemType Directory -Force $DestDir | Out-Null
    Copy-Item -LiteralPath $Source $dest -Force
    return $true
}

# Sets $Key=$Value in the SKSE plugin's ini, keeping its other lines; an empty $Value takes the
# key out. Written without a BOM: the plugin reads plain UTF-8. $true when it wrote.
function Set-PluginSetting([string] $IniPath, [string] $Key, [string] $Value) {
    $old = if (Test-Path -LiteralPath $IniPath) { @(Get-Content -LiteralPath $IniPath -Encoding UTF8) } else { @() }
    $others = @($old | Where-Object { $_ -notmatch "^\s*$([regex]::Escape($Key))\s*=" })
    $lines = if ($Value) { $others + "$Key=$Value" } else { $others }
    if ($Value -and $old -contains "$Key=$Value") { return $false }
    if (-not $Value -and $others.Count -eq $old.Count) { return $false }
    New-Item -ItemType Directory -Force (Split-Path $IniPath) | Out-Null
    [IO.File]::WriteAllLines($IniPath, [string[]] $lines, (New-Object Text.UTF8Encoding $false))
    return $true
}

# Points F11 screenshots at $Dir (ScreenshotDir in the SKSE plugin's ini).
function Set-ScreenshotDir([string] $IniPath, [string] $Dir) {
    return Set-PluginSetting $IniPath 'ScreenshotDir' $Dir
}

# Makes $Plugin active in a plugins.txt (Skyrim's own in %LOCALAPPDATA%, or an MO2 profile's).
# Read and written as Latin-1, byte for byte, so the other plugins' names keep whatever encoding
# they're in, and so do the line endings. $true when it wrote.
function Enable-SkyrimPlugin([string] $PluginsTxt, [string] $Plugin) {
    $latin1 = [Text.Encoding]::GetEncoding(28591)
    $text = if (Test-Path -LiteralPath $PluginsTxt) { $latin1.GetString([IO.File]::ReadAllBytes($PluginsTxt)) } else { '' }
    $newline = if ($text -match "`r`n" -or -not $text) { "`r`n" } else { "`n" }
    $lines = Get-EnabledPluginLines @($text -split "`r?`n" | Where-Object { $_ }) $Plugin
    if (-not $lines) { return $false }
    New-Item -ItemType Directory -Force (Split-Path $PluginsTxt) | Out-Null
    [IO.File]::WriteAllBytes($PluginsTxt, $latin1.GetBytes(($lines -join $newline) + $newline))
    return $true
}

# Copies a starter save's files from $StarterDir into $SavesDir when $SavesDir has no $Marker file
# yet, so never over the player's own. Copy-Item keeps the files' old dates, so a game's Continue
# still loads the player's newest save. $true when it copied.
function Add-StarterSave([string] $StarterDir, [string] $SavesDir, [string] $Marker) {
    if (-not (Test-Path -LiteralPath $StarterDir) -or (Test-Path -LiteralPath (Join-Path $SavesDir $Marker))) { return $false }
    New-Item -ItemType Directory -Force $SavesDir | Out-Null
    Copy-Item -Force (Join-Path $StarterDir '*') $SavesDir
    return $true
}

# --- Mod Organizer 2 ----------------------------------------------------------------------------

# Where MO2 instances live: one the player named (Mo2Path in PerkeleRim.ini, a portable one say)
# first, then the global ones in %LOCALAPPDATA%\ModOrganizer.
function Get-Mo2InstanceDirs([string] $Configured) {
    $dirs = @()
    if ($Configured) { $dirs += $Configured }
    $local = Join-Path $env:LOCALAPPDATA 'ModOrganizer'
    if (Test-Path -LiteralPath $local) { $dirs += Get-ChildItem -LiteralPath $local -Directory | ForEach-Object FullName }
    return $dirs
}

# The MO2 instances that manage this Skyrim. Per instance: its selected profile's folder, the SKSE
# plugin files its enabled mods bring (Address Library is often one), the enabled mods that have
# PerkeleRim's Skyrim files, the executable that runs this Skyrim's skse64_loader.exe, and whether
# the profile keeps its own saves. MO2 writes its files as UTF-8.
function Get-Mo2Instances([string] $SkyrimPath, [string[]] $Dirs) {
    foreach ($dir in $Dirs) {
        $ini = Join-Path $dir 'ModOrganizer.ini'
        if (-not (Test-Path -LiteralPath $ini)) { continue }
        $mo2 = Read-Mo2Ini (Get-Content -Raw -Encoding UTF8 -LiteralPath $ini) $dir
        if (-not (Test-SamePath $mo2.GamePath $SkyrimPath)) { continue }
        $profileDir = Join-Path $mo2.ProfilesDir $mo2.SelectedProfile
        $modlist = Join-Path $profileDir 'modlist.txt'
        $enabled = if (Test-Path -LiteralPath $modlist) { @(Get-Mo2EnabledMods (Get-Content -Raw -Encoding UTF8 -LiteralPath $modlist)) } else { @() }
        $settings = Join-Path $profileDir 'settings.ini'
        $localSaves = (Test-Path -LiteralPath $settings) -and [bool] (Select-String -LiteralPath $settings -Pattern '^\s*LocalSaves\s*=\s*true' -Quiet)
        $enabled = @($enabled | Where-Object { Test-Path -LiteralPath (Join-Path $mo2.ModsDir $_) })
        $portable = Test-Path -LiteralPath (Join-Path $dir 'portable.txt')
        [pscustomobject] @{
            Name           = if ($portable) { "portable instance in $dir" } else { Split-Path -Leaf $dir }
            ShortcutName   = if ($portable) { '' } else { Split-Path -Leaf $dir }
            Dir            = $dir
            ProfileDir     = $profileDir
            PluginFiles    = @(foreach ($mod in $enabled) { Get-FileNames (Join-Path $mo2.ModsDir "$mod\SKSE\Plugins") })
            PerkeleRimMods = @($enabled | Where-Object { (Test-Path -LiteralPath (Join-Path $mo2.ModsDir "$_\SKSE\Plugins\PerkeleRim.dll")) -or (Test-Path -LiteralPath (Join-Path $mo2.ModsDir "$_\PerkeleRim.esp")) })
            SkseTitle      = Find-Mo2SkseExecutable $mo2.Executables $SkyrimPath
            LocalSaves     = $localSaves
        }
    }
}

# --- My Summer Car's screen settings ------------------------------------------------------------

# The launcher starts MSC in a small window at the lowest quality, and Unity saves the screen mode
# it ran in to the registry when it quits, so MSC started on its own afterwards would come up
# like that. The player's own values are kept in a file while PerkeleRim runs MSC and put back
# once MSC has closed (Wait-MscExit, in a hidden watcher the launcher starts).
$MscRegistryKey = 'HKCU:\Software\Amistech\My Summer Car'
$MscScreenBackup = Join-Path $env:LOCALAPPDATA 'PerkeleRim\msc-screen.txt'
$MscScreenValuePattern = '^(Screenmanager |UnityGraphicsQuality|UnitySelectMonitor)'

# Unity's screen values under $Key (name with Unity's _h<hash> suffix = number), in name order.
function Get-MscScreenValues([string] $Key) {
    $values = [ordered] @{}
    $item = Get-Item -LiteralPath $Key -ErrorAction SilentlyContinue
    if (-not $item) { return $values }
    foreach ($name in @($item.GetValueNames() | Where-Object { $_ -match $MscScreenValuePattern } | Sort-Object)) {
        if ($item.GetValueKind($name) -eq 'DWord') { $values[$name] = [int] $item.GetValue($name) }
    }
    return $values
}

# Keeps the player's screen values in $File. A file that's there already holds the values from
# before an earlier PerkeleRim session whose restore hasn't run yet, so it stays. $true when it saved.
function Save-MscScreenSettings([string] $Key, [string] $File) {
    if (Test-Path -LiteralPath $File) { return $false }
    $values = Get-MscScreenValues $Key
    $lines = @("# My Summer Car's screen settings from before PerkeleRim started it ($Key). The launcher puts them back once MSC closes.")
    $lines += foreach ($name in $values.Keys) { "$name=$($values[$name])" }
    New-Item -ItemType Directory -Force (Split-Path $File) | Out-Null
    [IO.File]::WriteAllLines($File, [string[]] $lines, (New-Object Text.UTF8Encoding $false))
    return $true
}

# Puts the values in $File back under $Key, removes screen values that weren't there before, and
# deletes $File. $true when it restored.
function Restore-MscScreenSettings([string] $Key, [string] $File) {
    if (-not (Test-Path -LiteralPath $File)) { return $false }
    $saved = [ordered] @{}
    foreach ($line in [IO.File]::ReadAllLines($File)) {
        if ($line -notmatch '^([^#=][^=]*)=(-?\d+)\s*$') { continue }
        $name, $value = $Matches[1], [int] $Matches[2]
        if ($name -match $MscScreenValuePattern) { $saved[$name] = $value }
    }
    if (-not (Test-Path -LiteralPath $Key)) { New-Item -Path $Key -Force | Out-Null }
    foreach ($name in $saved.Keys) { New-ItemProperty -LiteralPath $Key -Name $name -PropertyType DWord -Value $saved[$name] -Force | Out-Null }
    foreach ($name in @((Get-MscScreenValues $Key).Keys)) {
        if (-not $saved.Contains($name)) { Remove-ItemProperty -LiteralPath $Key -Name $name }
    }
    # Another watcher may have restored and deleted it just now.
    Remove-Item -LiteralPath $File -Force -ErrorAction SilentlyContinue
    return $true
}

# Waits until no mysummercar process has run for $QuietSeconds. Steam may restart the game under a
# new pid, starting Steam itself first, so a gap of up to a minute isn't the end.
function Wait-MscExit([int] $QuietSeconds = 60) {
    $lastSeen = Get-Date
    while (((Get-Date) - $lastSeen).TotalSeconds -lt $QuietSeconds) {
        Start-Sleep -Seconds 2
        if (Get-Process mysummercar -ErrorAction SilentlyContinue) { $lastSeen = Get-Date }
    }
}

# Starts "$Launcher -RestoreMscScreen" in a hidden Windows PowerShell, which waits for MSC to close
# and puts the screen settings back. Windows PowerShell rebuilds its own module path; one
# inherited from PowerShell 7 breaks it.
function Start-MscScreenWatcher([string] $Launcher) {
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $modulePath = $env:PSModulePath
    try {
        $env:PSModulePath = $null
        Start-Process -WindowStyle Hidden -FilePath $powershell -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$Launcher`"", '-RestoreMscScreen'
    } finally { $env:PSModulePath = $modulePath }
}
