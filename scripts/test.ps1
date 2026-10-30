<#
.SYNOPSIS
    Runs the unit tests: the C++ protocol tests (CMake + doctest, ctest), the C# link tests
    (dotnet test) and the PowerShell tests (Pester 5: the player launcher's, launcher/tests, and the
    dev build helpers', scripts/tests) in PowerShell 7 and in Windows PowerShell 5.1, which the
    launcher runs on. None needs the games installed.
.EXAMPLE
    scripts/test.ps1                  # all three
    scripts/test.ps1 -Target cpp
    scripts/test.ps1 -Target launcher
    powershell.exe -File scripts/test.ps1   # from WSL
#>
param(
    [ValidateSet('all', 'cpp', 'cs', 'launcher')] [string] $Target = 'all'
)

. "$PSScriptRoot/common.ps1"

function Invoke-Checked {
    param([string] $File, [string[]] $Arguments)
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
}

if ($Target -in 'all', 'cpp') {
    Enter-DevShell
    if (-not $env:VCPKG_ROOT -and $env:VCPKG_INSTALLATION_ROOT) { $env:VCPKG_ROOT = $env:VCPKG_INSTALLATION_ROOT }  # GitHub runners
    if (-not $env:VCPKG_ROOT) { throw "VCPKG_ROOT is not set. Install vcpkg (docs/DEV_SETUP.md) or use the copy bundled with Build Tools." }
    Push-Location (Join-Path $RepoRoot 'protocol')
    try {
        Invoke-Checked cmake @('--preset', 'tests')
        Invoke-Checked cmake @('--build', '--preset', 'tests')
        Invoke-Checked ctest @('--preset', 'tests')
    } finally { Pop-Location }
}

if ($Target -in 'all', 'cs') {
    Invoke-Checked dotnet @('test', (Join-Path $RepoRoot 'msc/tests/PerkeleRim.Tests.csproj'), '-nologo')
}

if ($Target -in 'all', 'launcher') {
    # Pester 5 (GitHub's runners have it; Windows PowerShell itself ships only 3.4).
    if (Get-Module -ListAvailable Pester | Where-Object { $_.Version -ge [version] '5.0' }) {
        Import-Module Pester -MinimumVersion 5.0
        $pester = New-PesterConfiguration
        $pester.Run.Path = @((Join-Path $RepoRoot 'launcher/tests'), (Join-Path $RepoRoot 'scripts/tests'))
        $pester.Run.Exit = $false
        $pester.Run.PassThru = $true
        $pester.Output.Verbosity = 'Detailed'
        $result = Invoke-Pester -Configuration $pester
        if ($result.Result -ne 'Passed') { throw "Launcher tests: $($result.FailedCount) failed." }
        # Players run the launcher on Windows PowerShell 5.1 ("Start PerkeleRim.bat"), so the tests run there
        # as well, with the same Pester by path and, as the .bat does, no module path inherited from here.
        $windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        if ($PSVersionTable.PSEdition -eq 'Core' -and (Test-Path $windowsPowerShell)) {
            $pesterPath = (Get-Module Pester).Path
            $command = "Import-Module '$pesterPath'; `$c = New-PesterConfiguration; `$c.Run.Path = @('$($pester.Run.Path.Value -join "', '")'); `$c.Run.Exit = `$true; `$c.Output.Verbosity = 'Normal'; Invoke-Pester -Configuration `$c"
            $modulePath = $env:PSModulePath
            try { $env:PSModulePath = $null; & $windowsPowerShell -NoProfile -ExecutionPolicy Bypass -Command $command }
            finally { $env:PSModulePath = $modulePath }
            if ($LASTEXITCODE -ne 0) { throw "Launcher tests on Windows PowerShell 5.1: $LASTEXITCODE failed." }
        }
    } elseif ($env:CI) {
        throw 'Pester 5 is not installed, so the launcher tests could not run.'
    } else {
        Write-Host 'Launcher tests skipped: they need Pester 5 (Install-Module Pester -Scope CurrentUser -Force -SkipPublisherCheck).' -ForegroundColor Yellow
    }
}
