<#
.SYNOPSIS
    The launcher's checks as pure functions: they get what the launcher read from the disk (file
    versions, file names, ini text) and return what's wrong, so scripts/test.ps1 covers them
    without either game installed (launcher/tests/checks.Tests.ps1). Dot-source it.
#>

$Links = @{
    Skse           = 'https://skse.silverlock.org'
    AddressLibrary = 'https://www.nexusmods.com/skyrimspecialedition/mods/32444'
    MscLoader      = 'https://www.nexusmods.com/mysummercar/mods/147'
    Install        = 'https://github.com/rackmarten/PerkeleRim#install-and-play'
}

# The first Anniversary Edition runtime. Older ones are the Special Edition's, of which PerkeleRim
# runs on the last, 1.5.97.
$MinSkyrimVersion = [version] '1.6.317'
$MinSpecialEditionVersion = [version] '1.5.97'

# SkyrimSE.exe's FileVersion ("1.6.1170.0") and the SKSE and Address Library files made for it.
# Address Library's Special Edition file (1.5.x) has version-*.bin, its Anniversary Edition one
# versionlib-*.bin.
function Get-SkyrimRuntime([string] $FileVersion) {
    if ($FileVersion -notmatch '^\s*(\d+)[.,]\s*(\d+)[.,]\s*(\d+)') { return $null }
    $version = [version] "$($Matches[1]).$($Matches[2]).$($Matches[3])"
    $anniversary = $version -ge $MinSkyrimVersion
    $prefix = if ($anniversary) { 'versionlib' } else { 'version' }
    [pscustomobject] @{
        Version            = $version
        IsAnniversary      = $anniversary
        SkseDll            = "skse64_$($version.Major)_$($version.Minor)_$($version.Build).dll"
        AddressLibrary     = "$prefix-$($version.Major)-$($version.Minor)-$($version.Build)-0.bin"
        AddressLibraryFile = if ($anniversary) { 'All in one (Anniversary Edition)' } else { 'All in one (Special Edition)' }
    }
}

# What's wrong with Skyrim's side. GameFiles: file names in the Skyrim folder. PluginFiles: file
# names in Data\SKSE\Plugins, the real one and every enabled Mod Organizer 2 mod's.
function Get-SkyrimProblems([string] $FileVersion, [string[]] $GameFiles, [string[]] $PluginFiles) {
    $runtime = Get-SkyrimRuntime $FileVersion
    if (-not $runtime) { return @("Can't read SkyrimSE.exe's version. Check SkyrimPath in PerkeleRim.ini.") }
    $problems = @()
    if ($runtime.Version -lt $MinSpecialEditionVersion) {
        $problems += "Skyrim $($runtime.Version) is too old. PerkeleRim needs Special Edition $MinSpecialEditionVersion or Anniversary Edition ($MinSkyrimVersion or later): let Steam update Skyrim, then update SKSE and Address Library to match."
    }
    if ($GameFiles -notcontains 'skse64_loader.exe') {
        $problems += "SKSE64 is not installed in Skyrim. Get the build for Skyrim $($runtime.Version) from $($Links.Skse) and copy skse64_loader.exe and its DLLs into the Skyrim folder."
    } elseif ($GameFiles -notcontains $runtime.SkseDll) {
        $found = @($GameFiles | Where-Object { $_ -like 'skse64_1_*.dll' })
        $what = if ($found) { " (found $($found -join ', '))" } else { '' }
        $problems += "SKSE64 is for another Skyrim version$what, not $($runtime.Version) ($($runtime.SkseDll)). Get the matching build from $($Links.Skse)."
    }
    if ($PluginFiles -notcontains $runtime.AddressLibrary) {
        $problems += "Address Library for SKSE Plugins is not installed for Skyrim $($runtime.Version) ($($runtime.AddressLibrary)). Get the $($runtime.AddressLibraryFile) file from $($Links.AddressLibrary) and put it in Data\SKSE\Plugins (or install it with your mod manager)."
    }
    return $problems
}

# What's wrong with My Summer Car's side. GameFiles: file names in the MSC folder. ManagedFiles:
# file names in mysummercar_Data\Managed. MSCLoader's installer puts doorstop_config.ini and
# winhttp.dll in the game folder and MSCLoader.dll among Unity's assemblies.
function Get-MscProblems([string[]] $GameFiles, [string[]] $ManagedFiles) {
    if ($GameFiles -notcontains 'mysummercar.exe') { return @("mysummercar.exe is not in the My Summer Car folder. Check MscPath in PerkeleRim.ini.") }
    if ($GameFiles -notcontains 'doorstop_config.ini' -or $ManagedFiles -notcontains 'MSCLoader.dll') {
        return @("MSCLoader is not installed in My Summer Car. Get it from $($Links.MscLoader), run its installer and point it at the My Summer Car folder.")
    }
    return @()
}

# Where MSCLoader keeps mods, from doorstop_config.ini's "mods=" line: GF the game folder (also
# when there's no such line), MD Documents, AD AppData.
function Get-ModsDirSetting([string] $DoorstopText) {
    foreach ($line in ($DoorstopText -split "`r?`n")) {
        if ($line -match '^\s*mods\s*=\s*(\w+)') { return $Matches[1].ToUpperInvariant() }
    }
    return 'GF'
}

# plugins.txt's lines with PerkeleRim.esp active ("*Name.esp" after the official masters, in load
# order), or $null when it's active already. An inactive line for it is replaced.
function Get-EnabledPluginLines([string[]] $Lines, [string] $Plugin) {
    if (@($Lines) -contains "*$Plugin") { return $null }
    return @(@($Lines | Where-Object { $_ -and $_ -ne $Plugin }) + "*$Plugin")
}

# --- Mod Organizer 2 ----------------------------------------------------------------------------

# Reads a Mod Organizer 2 ModOrganizer.ini (QSettings): the game folder it manages, its mods and
# profiles folders, the selected profile and its executables (@{ Title; Binary }).
function Read-Mo2Ini([string] $IniText, [string] $InstanceDir) {
    $values = @{}
    $section = ''
    foreach ($line in ($IniText -split "`r?`n")) {
        if ($line -match '^\s*\[(.+)\]\s*$') { $section = $Matches[1]; continue }
        if ($line -match '^\s*([^=;#]+?)\s*=\s*(.*?)\s*$') {
            $key, $value = "$section/$($Matches[1])", $Matches[2]
            if ($value -match '^"(.*)"$') { $value = $Matches[1] }
            if ($value -match '^@ByteArray\((.*)\)$') { $value = $Matches[1] }
            $values[$key] = $value -replace '\\\\', '\'
        }
    }
    function Path-Of($value) { if ($value) { $value -replace '/', '\' } }
    $base = if ($values['Settings/base_directory']) { Path-Of $values['Settings/base_directory'] } else { $InstanceDir }
    $base = $base.TrimEnd('\')
    function Dir-Of($setting, $default) {
        $value = $values["Settings/$setting"]
        if ($value) { (Path-Of $value) -replace '%BASE_DIR%', $base } else { "$base\$default" }
    }
    $executables = @()
    $count = 0
    [void] [int]::TryParse($values['customExecutables/size'], [ref] $count)
    for ($i = 1; $i -le $count; $i++) {
        $title = $values["customExecutables/$i\title"]
        if ($title) { $executables += [pscustomobject] @{ Title = $title; Binary = Path-Of $values["customExecutables/$i\binary"] } }
    }
    $selected = $values['General/selected_profile']
    [pscustomobject] @{
        GamePath        = Path-Of $values['General/gamePath']
        ModsDir         = Dir-Of 'mod_directory' 'mods'
        ProfilesDir     = Dir-Of 'profiles_directory' 'profiles'
        SelectedProfile = if ($selected) { $selected } else { 'Default' }
        Executables     = $executables
    }
}

# The mods a profile's modlist.txt enables: "+Name" lines. "-Name" is a disabled mod, "*Name" an
# unmanaged one (DLC, loose files) with no folder of its own, "#" a comment.
function Get-Mo2EnabledMods([string] $ModlistText) {
    @(foreach ($line in ($ModlistText -split "`r?`n")) {
        if ($line -match '^\+(.+?)\s*$') { $Matches[1] }
    })
}

# The title of the instance's executable that runs this Skyrim's skse64_loader.exe ($null if none).
function Find-Mo2SkseExecutable($Executables, [string] $SkyrimPath) {
    $loader = "$(($SkyrimPath -replace '/', '\').TrimEnd('\'))\skse64_loader.exe"
    foreach ($exe in $Executables) {
        if (Test-SamePath $exe.Binary $loader) { return $exe.Title }
    }
    return $null
}

# MO2's shortcut link: moshortcut://<instance>:<executable title>. An empty instance name means
# the portable instance (MO2's MOShortcut::isForInstance).
function Get-Mo2ShortcutUri([string] $Instance, [string] $Title) {
    return "moshortcut://$($Instance):$Title"
}

# ModOrganizer.exe from a shell command such as the nxm:// handler's ("C:\MO2\nxmhandler.exe" "%1"):
# MO2's own nxmhandler.exe sits next to ModOrganizer.exe. $null for any other program.
function Get-Mo2ExeFromCommand([string] $Command) {
    if ($Command -notmatch '^\s*(?:"([^"]+)"|(\S+))') { return $null }
    $program = if ($Matches[1]) { $Matches[1] } else { $Matches[2] }
    $leaf = Split-Path -Leaf ($program -replace '/', '\')
    if ($leaf -notin 'nxmhandler.exe', 'ModOrganizer.exe') { return $null }
    $dir = $program.Substring(0, $program.Length - $leaf.Length).TrimEnd('\', '/')
    return "$dir\ModOrganizer.exe"
}

# What to tell a player who starts Skyrim through MO2. The launcher puts PerkeleRim's Skyrim files
# into Skyrim's own Data folder, which MO2 shows to Skyrim under its mods. PerkeleRimMods: enabled
# MO2 mods that have PerkeleRim.dll or PerkeleRim.esp, which win over the launcher's (and are
# usually an older release). SkseTitle: the instance's executable for skse64_loader.exe.
function Get-Mo2Warnings([string] $Instance, [string[]] $PerkeleRimMods, [string] $SkseTitle) {
    $warnings = @()
    if ($PerkeleRimMods) {
        $warnings += "MO2 mod $(($PerkeleRimMods | ForEach-Object { "'$_'" }) -join ', ') in '$Instance' has PerkeleRim's Skyrim files, and MO2 loads those instead of the ones this launcher installs in Skyrim's Data folder. Untick or remove it in MO2's left pane; the launcher keeps Data up to date."
    }
    if (-not $SkseTitle) {
        $warnings += "MO2 instance '$Instance' has no executable for skse64_loader.exe. In MO2: the gears icon (Modify Executables), add SKSE with Binary set to skse64_loader.exe in your Skyrim folder, then run the launcher again. Until then, start Skyrim from MO2 yourself."
    }
    return $warnings
}

# The same folder, whatever the slashes, case or a trailing separator.
function Test-SamePath([string] $A, [string] $B) {
    if (-not $A -or -not $B) { return $false }
    $trim = { param($p) ($p -replace '/', '\').TrimEnd('\') }
    return (& $trim $A) -eq (& $trim $B)
}
