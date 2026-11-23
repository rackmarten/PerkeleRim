# Test sessions with skse-test-kit (scripts/skytest.ps1, docs/DEV_SETUP.md "Test sessions"). Paths come from
# local.props: SkyrimPath, and GamesLock for the folder every session on this machine locks the games with.
@{
    Name     = 'PerkeleRim'
    Ini      = 'Data/SKSE/Plugins/PerkeleRim.ini'
    # What scripts/build.ps1 built last, both halves and the ESP, into both games; the player's own files are kept and
    # AhShitRim's plugin set aside (both take over the player) until Undeploy puts everything back (skytest stop).
    Deploy   = @{ Script = 'scripts/test-deploy.ps1'; Arguments = @('deploy', 'AhShitRim') }
    Undeploy = @{ Script = 'scripts/test-deploy.ps1'; Arguments = @('restore') }
    # What a build changes: skytest session starts again when these differ from the running session's.
    Build    = @('skse/build/release/PerkeleRim.dll', 'msc/bin/Release/net35/PerkeleRim.dll', 'tools/out/esp/PerkeleRim.esp')
    # My Summer Car beside Skyrim, minimized and never focused, continuing its save. MSC=1 only while it runs: the session
    # is ready once MSC has Kumpo standing on its island and has sent its models.
    Partner  = @{ Script = 'scripts/launch-msc.ps1'; Arguments = @{ Hidden = $true }; Processes = @('mysummercar'); Test = @{ MSC = '1' } }
    # The starter save (scripts/starter-save.ps1 -Skyrim): Kumpo at the garage; loading it hands him to MSC (play mode).
    Start    = 'save:PerkeleRim-Kumpo'
}
