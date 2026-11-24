# Pester 5 tests for scripts/test-session.ps1 (test sessions' file helpers), run by scripts/test.ps1.

BeforeAll {
    . (Join-Path $PSScriptRoot '..\test-session.ps1')
}

Describe 'Save-SessionFiles and Restore-SessionFiles' {
    BeforeEach {
        $game = Join-Path $TestDrive ([guid]::NewGuid())
        $store = Join-Path $game 'store'
        New-Item -ItemType Directory -Force (Join-Path $game 'Plugins') | Out-Null
        $ours = Join-Path $game 'Plugins\PerkeleRim.dll'
        $absent = Join-Path $game 'Plugins\PerkeleRim.pdb'
        $other = Join-Path $game 'Plugins\AhShitRim.dll'
        $missingOther = Join-Path $game 'Plugins\AhShitRim.pdb'
        [IO.File]::WriteAllText($ours, 'the player''s own')
        [IO.File]::WriteAllText($other, 'another mod')
    }

    It 'keeps, notes and sets aside, then puts everything back as it was' {
        Save-SessionFiles $store -Keep @($ours, $absent) -Move @($other, $missingOther) | Should -BeTrue
        Test-Path $other | Should -BeFalse  # set aside
        Test-Path $ours | Should -BeTrue    # only copied
        # The deploy writes over ours and adds the one that wasn't there.
        [IO.File]::WriteAllText($ours, 'the test build')
        [IO.File]::WriteAllText($absent, 'symbols')
        $back = Restore-SessionFiles $store
        $back.Count | Should -Be 3
        [IO.File]::ReadAllText($ours) | Should -Be 'the player''s own'
        Test-Path $absent | Should -BeFalse
        [IO.File]::ReadAllText($other) | Should -Be 'another mod'
        Test-Path $missingOther | Should -BeFalse
        Test-Path $store | Should -BeFalse
    }

    It 'keeps the first copies through a second deploy' {
        Save-SessionFiles $store -Keep @($ours) | Should -BeTrue
        [IO.File]::WriteAllText($ours, 'the test build')
        Save-SessionFiles $store -Keep @($ours) | Should -BeFalse
        [IO.File]::WriteAllText($ours, 'a newer test build')
        [void] (Restore-SessionFiles $store)
        [IO.File]::ReadAllText($ours) | Should -Be 'the player''s own'
    }

    It 'refuses to restore over files that changed since the deploy, unless forced' {
        Save-SessionFiles $store -Keep @($ours) | Should -BeTrue
        [IO.File]::WriteAllText($ours, 'the test build')
        Save-DeployStamps $store
        [IO.File]::WriteAllText($ours, 'a newer release the player installed')
        { Restore-SessionFiles $store } | Should -Throw '*changed since*'
        [IO.File]::ReadAllText($ours) | Should -Be 'a newer release the player installed'
        { Save-SessionFiles $store -Keep @($ours) } | Should -Throw '*changed since*'
        [void] (Restore-SessionFiles $store -Force)
        [IO.File]::ReadAllText($ours) | Should -Be 'the player''s own'
    }

    It "doesn't watch a file the game writes itself" {
        Save-SessionFiles $store -Keep @($ours, $absent) | Should -BeTrue
        [IO.File]::WriteAllText($ours, 'the test build')
        [IO.File]::WriteAllText($absent, 'a load order')
        Save-DeployStamps $store -Skip $absent
        [IO.File]::WriteAllText($absent, 'the load order as the game rewrote it')
        @(Get-ChangedSinceDeploy $store).Count | Should -Be 0
        [void] (Restore-SessionFiles $store)
        Test-Path $absent | Should -BeFalse
    }

    It 'refuses to go on when a file it set aside is back' {
        Save-SessionFiles $store -Keep @($ours) -Move @($other) | Should -BeTrue
        [IO.File]::WriteAllText($other, 'put back by someone')
        { Save-SessionFiles $store -Keep @($ours) -Move @($other) } | Should -Throw '*is back*'
    }

    It 'sets aside a file that turns up only by the second deploy' {
        Remove-Item $other
        Save-SessionFiles $store -Keep @($ours) -Move @($other) | Should -BeTrue
        [IO.File]::WriteAllText($other, 'deployed meanwhile')
        Save-SessionFiles $store -Keep @($ours) -Move @($other) | Should -BeFalse
        Test-Path $other | Should -BeFalse
        [void] (Restore-SessionFiles $store)
        [IO.File]::ReadAllText($other) | Should -Be 'deployed meanwhile'
    }

    It 'has nothing to put back without a list' {
        @(Restore-SessionFiles $store).Count | Should -Be 0
        Read-SessionList $store | Should -BeNullOrEmpty
    }
}

Describe 'Set-IniText' {
    It 'replaces keys in the section and keeps everything else, line endings included' {
        $text = "[General]`r`nenabled = true`r`n`r`n[MSCLoader]`r`nmods = GF`r`nskipIntro = false`r`nskipConfigScreen = false`r`n"
        Set-IniText $text 'MSCLoader' @{ skipIntro = 'true'; skipConfigScreen = 'true' } |
            Should -Be "[General]`r`nenabled = true`r`n`r`n[MSCLoader]`r`nmods = GF`r`nskipIntro = true`r`nskipConfigScreen = true`r`n"
    }

    It 'adds a missing key at the end of its section, before the blank lines' {
        $text = "[MSCLoader]`nmods = GF`n`n[Other]`nx = 1`n"
        Set-IniText $text 'MSCLoader' @{ skipIntro = 'true' } | Should -Be "[MSCLoader]`nmods = GF`nskipIntro = true`n`n[Other]`nx = 1`n"
    }

    It 'adds the section when there is none' {
        Set-IniText "[General]`nenabled = true`n" 'MSCLoader' @{ skipIntro = 'true' } | Should -Be "[General]`nenabled = true`n[MSCLoader]`nskipIntro = true`n"
    }
}

Describe 'Restore-MscDoorstop' {
    It 'puts the kept doorstop_config.ini back once' {
        $msc = Join-Path $TestDrive 'msc'
        $MscDoorstopKept = Join-Path $TestDrive 'kept\doorstop_config.ini.test-backup'  # the function reads it from here
        New-Item -ItemType Directory -Force $msc, (Split-Path $MscDoorstopKept) | Out-Null
        [IO.File]::WriteAllText($MscDoorstopKept, 'skipIntro = false')
        [IO.File]::WriteAllText((Join-Path $msc 'doorstop_config.ini'), 'skipIntro = true')
        Restore-MscDoorstop $msc | Should -BeTrue
        [IO.File]::ReadAllText((Join-Path $msc 'doorstop_config.ini')) | Should -Be 'skipIntro = false'
        Restore-MscDoorstop $msc | Should -BeFalse
    }
}

Describe 'A session''s own My Summer Car save' {
    BeforeEach {
        $root = Join-Path $TestDrive ([guid]::NewGuid())
        $saves = Join-Path $root 'My Summer Car'
        $chosen = Join-Path $root 'half-built'
        $store = Join-Path $root 'store'
        New-Item -ItemType Directory -Force $saves, $chosen | Out-Null
        foreach ($f in 'defaultES2File.txt', 'items.txt', 'meshsave.txt', 'options.txt') { [IO.File]::WriteAllText((Join-Path $saves $f), "player $f") }
        foreach ($f in 'defaultES2File.txt', 'items.txt', 'options.txt') { [IO.File]::WriteAllText((Join-Path $chosen $f), "test $f") }
    }

    It 'covers the save files of both, never the settings' {
        $names = @(Get-MscSavePaths $saves $chosen | ForEach-Object { Split-Path -Leaf $_ })
        $names | Should -Be @('defaultES2File.txt', 'items.txt', 'meshsave.txt')
    }

    It 'plays the chosen save and puts the player''s back on restore' {
        Save-SessionFiles $store -Keep @(Get-MscSavePaths $saves $chosen) | Should -BeTrue
        Use-MscSave $saves $chosen
        [IO.File]::ReadAllText((Join-Path $saves 'defaultES2File.txt')) | Should -Be 'test defaultES2File.txt'
        Test-Path (Join-Path $saves 'meshsave.txt') | Should -BeFalse  # not in the chosen save
        [IO.File]::ReadAllText((Join-Path $saves 'options.txt')) | Should -Be 'player options.txt'
        [void] (Restore-SessionFiles $store)
        foreach ($f in 'defaultES2File.txt', 'items.txt', 'meshsave.txt', 'options.txt') {
            [IO.File]::ReadAllText((Join-Path $saves $f)) | Should -Be "player $f"
        }
    }

    It 'takes a folder, and refuses one without a save' {
        Resolve-MscSave $chosen | Should -Be (Resolve-Path $chosen).Path
        Resolve-MscSave '' | Should -BeNullOrEmpty
        { Resolve-MscSave (Join-Path $root 'nothing') } | Should -Throw '*no My Summer Car save*'
    }
}
