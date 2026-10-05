# skse

SKSE plugin (C++, CommonLib) that runs inside Skyrim: collision sampling, NPC mirroring, rendering of the My Summer Car player and vehicles, crime and damage. See [docs/DESIGN.md](../docs/DESIGN.md).

It logs `PerkeleRim loaded` and the game runtime version to `Documents\My Games\Skyrim Special Edition\SKSE\PerkeleRim.log`, and starts the link to the MSC mod (`src/link`, built on `protocol/cpp`). Link state is logged there too.

F10 in game hands the player to My Summer Car and back (`src/player/PlayerSync.cpp`, see [docs/PROTOCOL.md](../docs/PROTOCOL.md#handing-over-the-player)); its log lines start with `player sync:`.

- `src/` plugin source
- `extern/CommonLibSSE-NG` git submodule ([alandtse/CommonLibSSE-NG](https://github.com/alandtse/CommonLibSSE-NG), pinned to a release tag)
- `vcpkg.json`, `vcpkg-configuration.json` dependency manifest and baseline
- `CMakePresets.json` `release` and `debug` presets (Ninja, MSVC, vcpkg)

Build with `scripts/build.ps1 -Target skse`. Setup and the reasons for the CommonLib choice are in [docs/DEV_SETUP.md](../docs/DEV_SETUP.md).
