# Pester 5 tests for launcher/install.ps1, run by scripts/test.ps1. Everything happens in TestDrive
# and a temporary registry key under HKCU:\Software\PerkeleRim-tests-*.

BeforeAll {
    . (Join-Path $PSScriptRoot '..\checks.ps1')
    . (Join-Path $PSScriptRoot '..\install.ps1')
    function New-Game([string] $Dir, [string] $Exe) {
        New-Item -ItemType Directory -Force $Dir | Out-Null
        Set-Content -LiteralPath (Join-Path $Dir $Exe) -Value 'exe'
        return $Dir
    }
}

Describe 'Get-FolderFromRegistryValue' {
    It 'takes the folder of a quoted exe path' {
        Get-FolderFromRegistryValue '"D:\Games\My Summer Car\mysummercar.exe"' | Should -Be 'D:\Games\My Summer Car'
    }
    It 'keeps a folder, without its trailing separator' {
        Get-FolderFromRegistryValue 'D:\SteamLibrary\steamapps\common\Skyrim Special Edition\' | Should -Be 'D:\SteamLibrary\steamapps\common\Skyrim Special Edition'
    }
    It 'returns nothing for an empty value' {
        Get-FolderFromRegistryValue '' | Should -BeNullOrEmpty
    }
}

Describe 'Finding the games in Steam libraries' {
    BeforeAll {
        # A drive letter this PC doesn't have, as when a library's disk was removed.
        $gone = [char[]] 'QRSTUVWXYZJKLMNOP' | Where-Object { -not (Test-Path "$($_):\") } | Select-Object -First 1
        $steam = New-Item -ItemType Directory "$TestDrive\Steam"
        $library = New-Item -ItemType Directory "$TestDrive\Library"
        New-Item -ItemType Directory "$steam\steamapps" | Out-Null
        Set-Content "$steam\steamapps\libraryfolders.vdf" @"
"libraryfolders"
{
    "0" { "path" "$("$steam" -replace '\\', '\\')" }
    "1" { "path" "$($gone):\\SteamLibrary" }
    "2" { "path" "$("$library" -replace '\\', '\\')" }
}
"@
        $msc = New-Game "$library\steamapps\common\My Summer Car" 'mysummercar.exe'
        $skyrim = New-Game "$steam\steamapps\common\Skyrim Special Edition" 'SkyrimSE.exe'
    }
    It 'lists every library, the gone one too, without throwing' {
        $libraries = @(Get-SteamLibraries "$steam")
        $libraries | Should -Contain "$steam"
        $libraries | Should -Contain "$($gone):\SteamLibrary"
        $libraries | Should -Contain "$library"
        @(Get-SteamLibraries "$steam".ToLowerInvariant()).Count | Should -Be 3
    }
    It 'guesses the gone library without throwing, and Select-GameInstalls drops it' {
        $guesses = @(Get-GameFolderGuesses $Games.Msc (Get-SteamLibraries "$steam"))
        $guesses | Should -Contain "$($gone):\SteamLibrary\steamapps\common\My Summer Car"
        @(Select-GameInstalls $guesses 'mysummercar.exe' | Where-Object { $_ -like "$($gone):*" }).Count | Should -Be 0
    }
    It 'finds each game in its own library' {
        $libraries = Get-SteamLibraries "$steam"
        Find-GameFolder $Games.Msc '' $libraries | Should -Be "$msc"
        Find-GameFolder $Games.Skyrim '' $libraries | Should -Be "$skyrim"
    }
    It 'keeps a configured folder that still has the game, and looks again when it does not' {
        $own = New-Game "$TestDrive\Own MSC" 'mysummercar.exe'
        Find-GameFolder $Games.Msc "$own" @() | Should -Be "$own"
        Find-GameFolder $Games.Msc "$TestDrive\Moved" (Get-SteamLibraries "$steam") | Should -Be "$msc"
        Find-GameFolder $Games.Msc "$($gone):\Games\MSC" @() | Should -BeNullOrEmpty
    }
    It 'lists a folder once, whatever its spelling, and skips bad names' {
        @(Select-GameInstalls @("$msc", "$msc\", "$("$msc".ToUpperInvariant())", 'bad|name', $null) 'mysummercar.exe').Count | Should -Be 1
    }
}

Describe 'Get-ModsDir' {
    It 'is the game folder by default, Documents with mods=MD' {
        $msc = New-Game "$TestDrive\MSC" 'mysummercar.exe'
        Get-ModsDir "$msc" | Should -Be "$msc\Mods"
        Set-Content "$msc\doorstop_config.ini" "[General]`nmods=MD"
        Get-ModsDir "$msc" | Should -Be (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'MySummerCar\Mods')
    }
}

Describe 'Installing files' {
    It 'copies a mod file only when it changed' {
        $source = "$TestDrive\files\PerkeleRim.dll"
        New-Item -ItemType Directory -Force (Split-Path $source) | Out-Null
        Set-Content $source 'v1'
        Install-ModFile $source "$TestDrive\Mods" | Should -BeTrue
        Install-ModFile $source "$TestDrive\Mods" | Should -BeFalse
        Set-Content $source 'v2'
        Install-ModFile $source "$TestDrive\Mods" | Should -BeTrue
        Get-Content "$TestDrive\Mods\PerkeleRim.dll" | Should -Be 'v2'
    }
    It 'sets ScreenshotDir, keeps the other lines, and writes no BOM' {
        $ini = "$TestDrive\Plugins\PerkeleRim.ini"
        New-Item -ItemType Directory -Force (Split-Path $ini) | Out-Null
        Set-Content $ini "Other=1`nScreenshotDir=C:\Old"
        Set-ScreenshotDir $ini 'C:\PerkeleRim\Screenshots' | Should -BeTrue
        Set-ScreenshotDir $ini 'C:\PerkeleRim\Screenshots' | Should -BeFalse
        Get-Content $ini | Should -Be @('Other=1', 'ScreenshotDir=C:\PerkeleRim\Screenshots')
        [IO.File]::ReadAllBytes($ini)[0] | Should -Be ([byte] [char] 'O')
    }
    It 'sets a plugin setting once and replaces it when it changes' {
        $ini = "$TestDrive\Plugins2\PerkeleRim.ini"
        New-Item -ItemType Directory -Force (Split-Path $ini) | Out-Null
        Set-Content $ini 'ScreenshotDir=C:\Shots'
        Set-PluginSetting $ini 'HelgenSide' 'Stormcloak' | Should -BeTrue
        Set-PluginSetting $ini 'HelgenSide' 'Stormcloak' | Should -BeFalse
        Get-Content $ini | Should -Be @('ScreenshotDir=C:\Shots', 'HelgenSide=Stormcloak')
        Set-PluginSetting $ini 'HelgenSide' 'Imperial' | Should -BeTrue
        Get-Content $ini | Should -Be @('ScreenshotDir=C:\Shots', 'HelgenSide=Imperial')
    }
    It 'leaves the plugin''s ini alone when a setting has no value' {
        $ini = "$TestDrive\Plugins3\PerkeleRim.ini"
        Set-PluginSetting $ini 'HelgenSide' '' | Should -BeFalse
        Test-Path $ini | Should -BeFalse
        New-Item -ItemType Directory -Force (Split-Path $ini) | Out-Null
        Set-Content $ini @('ScreenshotDir=C:\Shots', 'HelgenSide=Stormcloak')
        Set-PluginSetting $ini 'HelgenSide' '' | Should -BeFalse
        Get-Content $ini | Should -Be @('ScreenshotDir=C:\Shots', 'HelgenSide=Stormcloak')
    }
    It 'enables PerkeleRim.esp once, in a plugins.txt that is not there yet too' {
        $plugins = "$TestDrive\Local\Skyrim Special Edition\plugins.txt"
        Enable-SkyrimPlugin $plugins 'PerkeleRim.esp' | Should -BeTrue
        Enable-SkyrimPlugin $plugins 'PerkeleRim.esp' | Should -BeFalse
        Get-Content $plugins | Should -Be '*PerkeleRim.esp'
    }
    It 'keeps the other plugins'' bytes and the line endings in plugins.txt' {
        $plugins = "$TestDrive\MO2 profile\plugins.txt"
        New-Item -ItemType Directory -Force (Split-Path $plugins) | Out-Null
        # A UTF-8 BOM, a comment and a plugin named with a UTF-8 a-umlaut, CRLF line ends, as MO2 writes them.
        $before = [byte[]] (0xEF, 0xBB, 0xBF) + [Text.Encoding]::ASCII.GetBytes("# MO2`r`n*Kyl") + [byte[]] (0xC3, 0xA4) + [Text.Encoding]::ASCII.GetBytes(".esp`r`nPerkeleRim.esp`r`n")
        [IO.File]::WriteAllBytes($plugins, $before)
        Enable-SkyrimPlugin $plugins 'PerkeleRim.esp' | Should -BeTrue
        $after = [IO.File]::ReadAllBytes($plugins)
        $expected = [byte[]] (0xEF, 0xBB, 0xBF) + [Text.Encoding]::ASCII.GetBytes("# MO2`r`n*Kyl") + [byte[]] (0xC3, 0xA4) + [Text.Encoding]::ASCII.GetBytes(".esp`r`n*PerkeleRim.esp`r`n")
        [Convert]::ToBase64String($after) | Should -Be ([Convert]::ToBase64String($expected))
    }
    It 'adds a starter save only where there is none' {
        $starter = New-Item -ItemType Directory "$TestDrive\saves\MSC"
        Set-Content "$starter\defaultES2File.txt" 'starter'
        Add-StarterSave "$starter" "$TestDrive\LocalLow\MSC" 'defaultES2File.txt' | Should -BeTrue
        Set-Content "$TestDrive\LocalLow\MSC\defaultES2File.txt" 'the player''s own'
        Add-StarterSave "$starter" "$TestDrive\LocalLow\MSC" 'defaultES2File.txt' | Should -BeFalse
        Get-Content "$TestDrive\LocalLow\MSC\defaultES2File.txt" | Should -Be 'the player''s own'
        Add-StarterSave "$TestDrive\saves\Nothing" "$TestDrive\Saves2" 'x.ess' | Should -BeFalse
    }
}

Describe 'Get-Mo2Instances' {
    BeforeAll {
        $skyrim = New-Game "$TestDrive\Skyrim" 'SkyrimSE.exe'
        function New-Instance([string] $Dir, [string] $Game, [string[]] $Modlist, [switch] $Portable, [switch] $WithSkse) {
            New-Item -ItemType Directory -Force "$Dir\profiles\Default" | Out-Null
            $exes = if ($WithSkse) { "[customExecutables]`nsize=1`n1\title=SKSE`n1\binary=$($Game -replace '\\', '/')/skse64_loader.exe" } else { '' }
            Set-Content "$Dir\ModOrganizer.ini" "[General]`ngamePath=@ByteArray($($Game -replace '\\', '\\'))`n$exes"
            Set-Content "$Dir\profiles\Default\modlist.txt" ($Modlist -join "`n")
            if ($Portable) { Set-Content "$Dir\portable.txt" '' }
        }
        $mo2 = "$TestDrive\MO2"
        New-Instance $mo2 "$skyrim" @('+Address Library', '+PerkeleRim old', '-Disabled') -Portable -WithSkse
        foreach ($mod in 'Address Library', 'PerkeleRim old', 'Disabled') { New-Item -ItemType Directory -Force "$mo2\mods\$mod\SKSE\Plugins" | Out-Null }
        Set-Content "$mo2\mods\Address Library\SKSE\Plugins\versionlib-1-6-1170-0.bin" ''
        Set-Content "$mo2\mods\PerkeleRim old\SKSE\Plugins\PerkeleRim.dll" ''
        Set-Content "$mo2\mods\Disabled\SKSE\Plugins\Other.dll" ''
        New-Instance "$TestDrive\OtherGame" "$TestDrive\Fallout 4" @('+Address Library')
    }
    It 'finds the instance for this Skyrim with its enabled mods'' plugin files' {
        $instances = @(Get-Mo2Instances "$skyrim" @($mo2, "$TestDrive\OtherGame", "$TestDrive\NoInstance"))
        $instances.Count | Should -Be 1
        $instances[0].Name | Should -Be "portable instance in $mo2"
        $instances[0].ShortcutName | Should -Be ''
        $instances[0].ProfileDir | Should -Be "$mo2\profiles\Default"
        $instances[0].PluginFiles | Should -Contain 'versionlib-1-6-1170-0.bin'
        $instances[0].PluginFiles | Should -Not -Contain 'Other.dll'
        $instances[0].PerkeleRimMods | Should -Be @('PerkeleRim old')
        $instances[0].SkseTitle | Should -Be 'SKSE'
        $instances[0].LocalSaves | Should -BeFalse
    }
    It 'sees a profile that keeps its own saves' {
        Set-Content "$mo2\profiles\Default\settings.ini" "[General]`nLocalSaves=true"
        (@(Get-Mo2Instances "$skyrim" @($mo2))[0]).LocalSaves | Should -BeTrue
        Remove-Item "$mo2\profiles\Default\settings.ini"
    }
    It 'reads a non-ASCII game folder and mod name from MO2''s UTF-8 files' {
        # Built from a char code: Windows PowerShell reads this file as ANSI.
        $a = [string] [char] 0xE4
        $finnish = New-Game "$TestDrive\Pelit\Skyrim P$($a)$($a)si$($a)inen" 'SkyrimSE.exe'
        $dir = "$TestDrive\MO2 Finnish"
        New-Instance $dir "$finnish" @("+Osoitekirjasto $a")
        New-Item -ItemType Directory -Force "$dir\mods\Osoitekirjasto $a\SKSE\Plugins" | Out-Null
        Set-Content "$dir\mods\Osoitekirjasto $a\SKSE\Plugins\versionlib-1-6-1170-0.bin" ''
        foreach ($file in "$dir\ModOrganizer.ini", "$dir\profiles\Default\modlist.txt") {
            [IO.File]::WriteAllText($file, (Get-Content -Raw -LiteralPath $file), (New-Object Text.UTF8Encoding $false))  # Set-Content wrote ANSI on 5.1
        }
        @(Get-Mo2Instances "$finnish" @($dir))[0].PluginFiles | Should -Contain 'versionlib-1-6-1170-0.bin'
    }
    It 'puts the configured instance first' {
        # @(): with no global instances (CI) the one folder comes back as a plain string.
        @(Get-Mo2InstanceDirs 'D:\Portable MO2')[0] | Should -Be 'D:\Portable MO2'
    }
}

Describe "My Summer Car's screen settings" {
    BeforeAll {
        $key = "HKCU:\Software\PerkeleRim-tests-$([guid]::NewGuid().ToString('N'))"
        $backup = "$TestDrive\PerkeleRim\msc-screen.txt"
        function Set-Dword($name, $value) { New-ItemProperty -LiteralPath $key -Name $name -PropertyType DWord -Value $value -Force | Out-Null }
    }
    AfterAll { Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue }
    BeforeEach {
        Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
        New-Item -Path $key -Force | Out-Null
        Set-Dword 'Screenmanager Resolution Width_h182942802' 2560
        Set-Dword 'Screenmanager Resolution Height_h2627697771' 1440
        Set-Dword 'Screenmanager Is Fullscreen mode_h3981298716' 1
        Set-Dword 'UnityGraphicsQuality_h1669003810' 5
        New-ItemProperty -LiteralPath $key -Name 'PlayerName_h123' -PropertyType Binary -Value ([byte[]] (75, 0)) -Force | Out-Null
    }
    It 'reads only the screen values' {
        $values = Get-MscScreenValues $key
        $values.Count | Should -Be 4
        $values['Screenmanager Resolution Width_h182942802'] | Should -Be 2560
    }
    It 'puts the player''s values back after a PerkeleRim session' {
        Save-MscScreenSettings $key $backup | Should -BeTrue
        # What Unity saves after running at 640x480 windowed, "Fastest".
        Set-Dword 'Screenmanager Resolution Width_h182942802' 640
        Set-Dword 'Screenmanager Resolution Height_h2627697771' 480
        Set-Dword 'Screenmanager Is Fullscreen mode_h3981298716' 0
        Set-Dword 'UnityGraphicsQuality_h1669003810' 0
        Set-Dword 'UnitySelectMonitor_h17969598' 0
        Restore-MscScreenSettings $key $backup | Should -BeTrue
        $values = Get-MscScreenValues $key
        $values['Screenmanager Resolution Width_h182942802'] | Should -Be 2560
        $values['Screenmanager Resolution Height_h2627697771'] | Should -Be 1440
        $values['Screenmanager Is Fullscreen mode_h3981298716'] | Should -Be 1
        $values['UnityGraphicsQuality_h1669003810'] | Should -Be 5
        $values.Contains('UnitySelectMonitor_h17969598') | Should -BeFalse
        (Get-ItemProperty -LiteralPath $key).'PlayerName_h123' | Should -Be @(75, 0)
        Test-Path $backup | Should -BeFalse
    }
    It 'keeps the first backup while its restore has not run' {
        Save-MscScreenSettings $key $backup | Should -BeTrue
        Set-Dword 'Screenmanager Resolution Width_h182942802' 640
        Save-MscScreenSettings $key $backup | Should -BeFalse
        Restore-MscScreenSettings $key $backup | Should -BeTrue
        (Get-MscScreenValues $key)['Screenmanager Resolution Width_h182942802'] | Should -Be 2560
    }
    It 'takes out what Unity added when MSC had no screen settings before' {
        Remove-Item -LiteralPath $key -Recurse -Force
        Save-MscScreenSettings $key $backup | Should -BeTrue
        New-Item -Path $key -Force | Out-Null
        Set-Dword 'Screenmanager Resolution Width_h182942802' 640
        Restore-MscScreenSettings $key $backup | Should -BeTrue
        (Get-MscScreenValues $key).Count | Should -Be 0
    }
    It 'does nothing without a backup' {
        Restore-MscScreenSettings $key $backup | Should -BeFalse
        (Get-MscScreenValues $key)['Screenmanager Resolution Width_h182942802'] | Should -Be 2560
    }
}
