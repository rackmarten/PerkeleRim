#pragma once

// Agent-driven test sessions through skse-test-kit (skse/extern/skse-test-kit, docs/DEV_SETUP.md "Test sessions").
// Only dev builds link the kit (CMake option PERKELERIM_TEST_KIT, which scripts/build.ps1 turns on when the submodule
// is there and scripts/package.ps1 leaves off); without it these are no-ops. With the kit, PerkeleRim.ini's [Test]
// section (which scripts/skytest.ps1 writes for a session and takes out again) starts Skyrim hidden, never focused,
// straight into a test state; no [Test], no change at all. [Test] MSC=1 (My Summer Car runs beside it) makes the
// session ready only once MSC has the player, stands him on its island and has sent its models.
namespace PerkeleRim::TestKit
{
    void Load();  // first thing after SKSE::Init, before the game creates its window
    void OnMessage(SKSE::MessagingInterface::Message* a_message);
    bool Active();  // a test session runs: Autoload leaves the main menu to the kit
}
