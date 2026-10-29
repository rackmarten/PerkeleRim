# Pester 5 tests for scripts/dev-build.ps1 (scripts/publish-dev.ps1's helpers), run by scripts/test.ps1.

BeforeAll {
    . (Join-Path $PSScriptRoot '..\dev-build.ps1')
    $commit = '1541b0a3c2d4e5f60718293a4b5c6d7e8f901234'
}

Describe 'Get-NextVersion' {
    It 'takes the next minor once the version is released' {
        Get-NextVersion '0.1.1' @('v0.1.0', 'v0.1.1') | Should -Be '0.2.0'
    }
    It 'keeps a version bumped for a release that has no tag yet' {
        Get-NextVersion '0.2.0' @('v0.1.0', 'v0.1.1') | Should -Be '0.2.0'
    }
    It 'works with no tags at all' {
        Get-NextVersion '0.1.0' @() | Should -Be '0.1.0'
    }
    It "isn't fooled by the dev tag" {
        Get-NextVersion '0.1.1' @('dev', 'v0.1.0') | Should -Be '0.1.1'
    }
}

Describe 'Get-DevBuild' {
    BeforeAll { $build = Get-DevBuild '0.2.0' ([datetime] '2026-10-29 23:59') $commit }

    It 'marks the version as a pre-release of the next version' {
        $build.Version | Should -Be '0.2.0-dev.20261029'
    }
    It 'names the zip after the date and the short commit' {
        $build.Name | Should -Be 'dev-20261029-1541b0a'
        $build.Zip | Should -Be 'PerkeleRim-dev-20261029-1541b0a.zip'
        $build.Short | Should -Be '1541b0a'
    }
    It "gives a zip release-check.ps1 wouldn't take for a release" {
        $build.Zip | Should -Not -Match '^PerkeleRim-\d+\.\d+\.\d+\.zip$'
    }
}

Describe 'Get-CheckoutProblems' {
    It 'passes a clean main that is origin/main' {
        Get-CheckoutProblems 'main' @() $commit $commit | Should -BeNullOrEmpty
    }
    It 'ignores the empty line git status leaves' {
        Get-CheckoutProblems 'main' @('') $commit $commit | Should -BeNullOrEmpty
    }
    It 'refuses another branch' {
        Get-CheckoutProblems 'add-gifu' @() $commit $commit | Should -Match "on 'add-gifu', not main"
    }
    It 'refuses a detached HEAD' {
        Get-CheckoutProblems '' @() $commit $commit | Should -Match 'not main'
    }
    It 'refuses local changes and lists them' {
        $problems = Get-CheckoutProblems 'main' @(' M CHANGELOG.md', '?? notes.txt') $commit $commit
        $problems | Should -HaveCount 1
        $problems | Should -Match 'local changes'
        $problems | Should -Match 'notes\.txt'
    }
    It "refuses a main that isn't origin's" {
        Get-CheckoutProblems 'main' @() $commit ('f' * 40) | Should -Match "HEAD \(1541b0a\) isn't origin/main \(fffffff\)"
    }
    It 'asks for a fetch without origin/main' {
        Get-CheckoutProblems 'main' @() $commit '' | Should -Match 'git fetch origin'
    }
    It 'lists every problem at once' {
        Get-CheckoutProblems 'other' @(' M x') $commit ('f' * 40) | Should -HaveCount 3
    }
}

Describe 'Get-StaleDevAssets' {
    It 'drops older dev zips and keeps the new one and SHA256SUMS.txt' {
        $assets = @('PerkeleRim-dev-20261020-aaaaaaa.zip', 'SHA256SUMS.txt', 'PerkeleRim-dev-20261029-1541b0a.zip', 'PerkeleRim-dev-20261021-bbbbbbb.zip')
        Get-StaleDevAssets $assets 'PerkeleRim-dev-20261029-1541b0a.zip' | Should -Be @('PerkeleRim-dev-20261020-aaaaaaa.zip', 'PerkeleRim-dev-20261021-bbbbbbb.zip')
    }
    It 'never drops a release zip' {
        Get-StaleDevAssets @('PerkeleRim-0.1.1.zip') 'PerkeleRim-dev-20261029-1541b0a.zip' | Should -BeNullOrEmpty
    }
    It 'handles a release with no assets' {
        Get-StaleDevAssets @() 'PerkeleRim-dev-20261029-1541b0a.zip' | Should -BeNullOrEmpty
    }
}
