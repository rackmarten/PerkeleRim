# msc

MSCLoader mod (C#, .NET Framework 3.5) that runs inside My Summer Car: builds the collision island, runs the player and vehicles, reads PlayMaker FSM state. See [docs/DESIGN.md](../docs/DESIGN.md).

At the main menu it prints `PerkeleRim loaded` and starts the link to the Skyrim plugin (`src/Game/LinkHost.cs`); link state shows up in the MSCLoader console and `output_log.txt`.

- `src/Link` link to the Skyrim plugin (Unity-free, mirror of `protocol/`)
- `src/Game` code that needs Unity (real builds only)
- `src/Spike` hidden-MSC spike tools (real builds only)
- `tests/` NUnit tests for `src/Link` (net8, run by `scripts/test.ps1`)
- `PerkeleRim.csproj` SDK-style project, `net35`, references `MSCLoader.dll` and `UnityEngine.dll` from your game install (`local.props`)
- `stubs/MSCLoader` compile-only stand-in for `MSCLoader.dll`, used by CI (`-UseStubs`)

Build with `scripts/build.ps1 -Target msc`. Setup is in [docs/DEV_SETUP.md](../docs/DEV_SETUP.md).
