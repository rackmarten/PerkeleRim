<#
.SYNOPSIS
    Test sessions (docs/DEV_SETUP.md, "Test sessions"): starts Skyrim hidden and never focused, straight into a test
    state, with My Summer Car minimized beside it, and stops both again. Forwards to skse-test-kit's own
    scripts/skytest.ps1 (the skse/extern/skse-test-kit submodule) with every argument; skytest.psd1 holds PerkeleRim's
    settings. Build a dev build first (scripts/build.ps1 links the kit when the submodule is there).
.EXAMPLE
    scripts/skytest.ps1 session                       # the starter save, Kumpo handed to MSC
    scripts/skytest.ps1 cmd pr.vehicle van            # a command: one line of JSON back
    scripts/skytest.ps1 run scenarios/hits.json       # the hit test, results in out/<run>/
    scripts/skytest.ps1 start -NoPartner -Start coc:Riverwood   # Skyrim alone
    scripts/skytest.ps1 status
    scripts/skytest.ps1 stop                          # closes both, puts the game's own files back
#>
$kit = Join-Path (Split-Path -Parent $PSScriptRoot) 'skse/extern/skse-test-kit/scripts/skytest.ps1'
if (-not (Test-Path $kit)) {
    throw 'skse-test-kit is not checked out: git submodule update --init --checkout skse/extern/skse-test-kit (then scripts/build.ps1).'
}
if ($args -contains '-Mod') { & $kit @args } else { & $kit -Mod (Split-Path -Parent $PSScriptRoot) @args }
exit $LASTEXITCODE  # the kit's: run and cmd exit 1 on a failed check, 2 on a crash or hang
