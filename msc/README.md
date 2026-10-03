# msc

MSCLoader mod (C#, .NET Framework 3.5) that runs inside My Summer Car: builds the collision island, runs the player and vehicles, reads PlayMaker FSM state. See [docs/DESIGN.md](../docs/DESIGN.md).

Currently a hello-world: it prints `SkyrimSummerCar loaded` to the MSCLoader console on the main menu.

- `src/` mod source
- `SkyrimSummerCar.csproj` SDK-style project, `net35`, references `MSCLoader.dll` and `UnityEngine.dll` from your game install (`local.props`)
- `stubs/MSCLoader` compile-only stand-in for `MSCLoader.dll`, used by CI (`-UseStubs`)

Build with `scripts/build.ps1 -Target msc`. Setup is in [docs/DEV_SETUP.md](../docs/DEV_SETUP.md).
