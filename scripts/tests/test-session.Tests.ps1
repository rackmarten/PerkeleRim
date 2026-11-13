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
