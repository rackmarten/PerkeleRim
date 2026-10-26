# Pester 5 tests for launcher/checks.ps1, run by scripts/test.ps1.

BeforeAll {
    . (Join-Path $PSScriptRoot '..\checks.ps1')
    $aePlugins = @('versionlib-1-6-1170-0.bin')
    $aeGame = @('SkyrimSE.exe', 'skse64_loader.exe', 'skse64_1_6_1170.dll')
}

Describe 'Get-SkyrimRuntime' {
    It 'names the SKSE and Address Library files for an AE version' {
        $runtime = Get-SkyrimRuntime '1.6.1170.0'
        $runtime.Version | Should -Be ([version] '1.6.1170')
        $runtime.IsAnniversary | Should -BeTrue
        $runtime.SkseDll | Should -Be 'skse64_1_6_1170.dll'
        $runtime.AddressLibrary | Should -Be 'versionlib-1-6-1170-0.bin'
        $runtime.AddressLibraryFile | Should -Be 'All in one (Anniversary Edition)'
    }
    It "names Address Library's Special Edition file for 1.5.97" {
        $runtime = Get-SkyrimRuntime '1.5.97.0'
        $runtime.IsAnniversary | Should -BeFalse
        $runtime.SkseDll | Should -Be 'skse64_1_5_97.dll'
        $runtime.AddressLibrary | Should -Be 'version-1-5-97-0.bin'
        $runtime.AddressLibraryFile | Should -Be 'All in one (Special Edition)'
    }
    It 'reads a comma-separated version' {
        (Get-SkyrimRuntime '1, 6, 640, 0').Version | Should -Be ([version] '1.6.640')
    }
    It 'returns nothing for an unreadable version' {
        Get-SkyrimRuntime '' | Should -BeNullOrEmpty
        Get-SkyrimRuntime 'abc' | Should -BeNullOrEmpty
    }
}

Describe 'Get-SkyrimProblems' {
    It 'finds nothing wrong with a ready AE install' {
        @(Get-SkyrimProblems '1.6.1170.0' $aeGame $aePlugins).Count | Should -Be 0
    }
    It 'finds nothing wrong with a ready SE 1.5.97 install' {
        @(Get-SkyrimProblems '1.5.97.0' @('SkyrimSE.exe', 'skse64_loader.exe', 'skse64_1_5_97.dll') @('version-1-5-97-0.bin')).Count | Should -Be 0
    }
    It 'says SKSE is missing' {
        $problems = @(Get-SkyrimProblems '1.6.1170.0' @('SkyrimSE.exe') $aePlugins)
        $problems.Count | Should -Be 1
        $problems[0] | Should -BeLike 'SKSE64 is not installed*https://skse.silverlock.org*'
    }
    It 'says SKSE is for another version, naming the one found' {
        $problems = @(Get-SkyrimProblems '1.6.1170.0' @('SkyrimSE.exe', 'skse64_loader.exe', 'skse64_1_6_640.dll') $aePlugins)
        $problems[0] | Should -BeLike '*another Skyrim version (found skse64_1_6_640.dll)*skse64_1_6_1170.dll*'
    }
    It 'says Address Library is missing, with the file to get' {
        $problems = @(Get-SkyrimProblems '1.5.97.0' @('skse64_loader.exe', 'skse64_1_5_97.dll') @('versionlib-1-6-1170-0.bin'))
        $problems.Count | Should -Be 1
        $problems[0] | Should -BeLike '*version-1-5-97-0.bin*All in one (Special Edition)*nexusmods.com/skyrimspecialedition/mods/32444*'
    }
    It 'turns away a Skyrim older than 1.5.97' {
        @(Get-SkyrimProblems '1.5.80.0' @('skse64_loader.exe', 'skse64_1_5_80.dll') @('version-1-5-80-0.bin'))[0] | Should -BeLike '*1.5.80 is too old*'
    }
    It 'asks for the Skyrim folder when the version is unreadable' {
        @(Get-SkyrimProblems '' $aeGame $aePlugins)[0] | Should -BeLike '*SkyrimPath in PerkeleRim.ini*'
    }
}

Describe 'Get-MscProblems' {
    It 'finds nothing wrong with MSCLoader installed' {
        @(Get-MscProblems @('mysummercar.exe', 'doorstop_config.ini', 'winhttp.dll') @('MSCLoader.dll', 'Assembly-CSharp.dll')).Count | Should -Be 0
    }
    It 'says MSCLoader is missing without doorstop_config.ini or MSCLoader.dll' {
        @(Get-MscProblems @('mysummercar.exe') @('MSCLoader.dll'))[0] | Should -BeLike 'MSCLoader is not installed*nexusmods.com/mysummercar/mods/147*'
        @(Get-MscProblems @('mysummercar.exe', 'doorstop_config.ini') @('Assembly-CSharp.dll'))[0] | Should -BeLike 'MSCLoader is not installed*'
    }
    It 'asks for the MSC folder when it has no mysummercar.exe' {
        @(Get-MscProblems @() @())[0] | Should -BeLike '*MscPath in PerkeleRim.ini*'
    }
}

Describe 'Get-ModsDirSetting' {
    It 'reads where MSCLoader keeps mods' {
        Get-ModsDirSetting "[General]`r`nenabled=true`r`nmods=MD`r`n" | Should -Be 'MD'
        Get-ModsDirSetting "mods = ad" | Should -Be 'AD'
    }
    It 'defaults to the game folder' {
        Get-ModsDirSetting "[General]`nenabled=true" | Should -Be 'GF'
        Get-ModsDirSetting '' | Should -Be 'GF'
    }
}

Describe 'Get-EnabledPluginLines' {
    It 'appends PerkeleRim.esp as active' {
        Get-EnabledPluginLines @('*Unofficial Skyrim Special Edition Patch.esp') 'PerkeleRim.esp' | Should -Be @('*Unofficial Skyrim Special Edition Patch.esp', '*PerkeleRim.esp')
    }
    It 'turns an inactive line active' {
        Get-EnabledPluginLines @('PerkeleRim.esp', '*Other.esp') 'PerkeleRim.esp' | Should -Be @('*Other.esp', '*PerkeleRim.esp')
    }
    It 'returns nothing when it is active already' {
        Get-EnabledPluginLines @('*PerkeleRim.esp') 'PerkeleRim.esp' | Should -BeNullOrEmpty
    }
    It 'starts an empty plugins.txt' {
        Get-EnabledPluginLines @() 'PerkeleRim.esp' | Should -Be @('*PerkeleRim.esp')
    }
}

Describe 'Read-Mo2Ini' {
    It 'reads the game, folders, profile and executables' {
        $ini = @'
[General]
gameName=Skyrim Special Edition
gamePath=@ByteArray(C:\\Games\\Steam\\steamapps\\common\\Skyrim Special Edition)
selected_profile=@ByteArray(Survival)

[Settings]
base_directory=D:/MO2/SSE
mod_directory=%BASE_DIR%/mods

[customExecutables]
size=2
1\title=SKSE
1\binary=C:/Games/Steam/steamapps/common/Skyrim Special Edition/skse64_loader.exe
2\title=Skyrim Special Edition
2\binary=C:/Games/Steam/steamapps/common/Skyrim Special Edition/SkyrimSE.exe
'@
        $mo2 = Read-Mo2Ini $ini 'C:\Users\x\AppData\Local\ModOrganizer\Skyrim Special Edition'
        $mo2.GamePath | Should -Be 'C:\Games\Steam\steamapps\common\Skyrim Special Edition'
        $mo2.ModsDir | Should -Be 'D:\MO2\SSE\mods'
        $mo2.ProfilesDir | Should -Be 'D:\MO2\SSE\profiles'
        $mo2.SelectedProfile | Should -Be 'Survival'
        $mo2.Executables.Count | Should -Be 2
        Find-Mo2SkseExecutable $mo2.Executables 'C:\Games\Steam\steamapps\common\Skyrim Special Edition\' | Should -Be 'SKSE'
    }
    It 'defaults to the instance folder and the Default profile' {
        $mo2 = Read-Mo2Ini "[General]`ngamePath=C:\\Skyrim" 'D:\MO2'
        $mo2.ModsDir | Should -Be 'D:\MO2\mods'
        $mo2.SelectedProfile | Should -Be 'Default'
        Find-Mo2SkseExecutable $mo2.Executables 'C:\Skyrim' | Should -BeNullOrEmpty
    }
}

Describe 'Mod Organizer 2 helpers' {
    It 'lists only enabled mods' {
        Get-Mo2EnabledMods "# comment`n+Address Library`n-Old Thing`n*DLC: Dawnguard`n+SkyUI " | Should -Be @('Address Library', 'SkyUI')
    }
    It 'makes the shortcut link, empty for a portable instance' {
        Get-Mo2ShortcutUri 'Skyrim Special Edition' 'SKSE' | Should -Be 'moshortcut://Skyrim Special Edition:SKSE'
        Get-Mo2ShortcutUri '' 'SKSE' | Should -Be 'moshortcut://:SKSE'
    }
    It 'finds ModOrganizer.exe from the nxm handler' {
        Get-Mo2ExeFromCommand '"D:\MO2\nxmhandler.exe" "%1"' | Should -Be 'D:\MO2\ModOrganizer.exe'
        Get-Mo2ExeFromCommand '"C:\Vortex\Vortex.exe" -d "%1"' | Should -BeNullOrEmpty
    }
    It 'warns about an MO2 mod with PerkeleRim in it and a missing SKSE executable' {
        $warnings = @(Get-Mo2Warnings 'SSE' @('PerkeleRim 0.1.0') $null)
        $warnings.Count | Should -Be 2
        $warnings[0] | Should -BeLike "*'PerkeleRim 0.1.0'*instead of the ones this launcher installs*"
        $warnings[1] | Should -BeLike '*no executable for skse64_loader.exe*'
        @(Get-Mo2Warnings 'SSE' @() 'SKSE').Count | Should -Be 0
    }
    It 'compares folders whatever the slashes, case or trailing separator' {
        Test-SamePath 'C:/Games/Skyrim/' 'c:\games\skyrim' | Should -BeTrue
        Test-SamePath 'C:\Games\Skyrim' 'C:\Games\Skyrim2' | Should -BeFalse
        Test-SamePath $null 'C:\Games' | Should -BeFalse
    }
}
