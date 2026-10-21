# Windows development setup

Everything needed to build both halves of PerkeleRim, deploy them into your game installs and read their logs. Follow it top to bottom on a fresh Windows 10/11 machine.

The toolchain is deliberately small: **Build Tools for Visual Studio 2022** (compiler only, no IDE) for the C++ SKSE plugin, and the **.NET 8 SDK** for the C# MSCLoader mod. Visual Studio itself is optional.

## 1. Games

You need your own copies of both games. Nothing from them is ever committed to this repo.

### Skyrim Anniversary Edition

1. Install **The Elder Scrolls V: Skyrim Special Edition** from Steam, with the Anniversary upgrade if you own it. Keep it on the latest Steam version.
2. Check the runtime version: right-click `SkyrimSE.exe` > Properties > Details > File version. This guide was written against **1.7.104**. SKSE and Address Library must match it exactly.
3. Stop Steam from updating Skyrim behind your back: Steam > Skyrim > Properties > Updates > "Only update this game when I launch it", and launch through SKSE (below), not Steam. A Skyrim update breaks SKSE until SKSE ships a matching build.
4. Install **SKSE64** for your runtime from <https://skse.silverlock.org/> (the "Current Anniversary Edition build", 2.3.1 for 1.7.104). Copy `skse64_loader.exe`, `skse64_1_7_104.dll` and the `Data` folder contents into the Skyrim folder (next to `SkyrimSE.exe`).
5. Install **Address Library for SKSE Plugins** from Nexus (<https://www.nexusmods.com/skyrimspecialedition/mods/32444>), the "All in one (Anniversary Edition)" file. Its `SKSE/Plugins/versionlib-*.bin` files go into `Data/SKSE/Plugins`. Our plugin uses it to find game addresses, so it keeps working across game updates once Address Library is updated.
6. Launch `skse64_loader.exe` once, quit, and confirm `Documents\My Games\Skyrim Special Edition\SKSE\skse64.log` exists.

### My Summer Car + MSCLoader

1. Install **My Summer Car** from Steam and start it once.
2. Download the latest **MSCLoader** release from <https://github.com/piotrulos/MSCModLoader/releases> (this guide used 1.4.2). Unpack it somewhere outside the game folder.
3. Run `MSCPatcher.exe`, point "MSC Folder" at the game folder (`...\steamapps\common\My Summer Car`), keep the Mods folder at **Game Folder** (recommended), and install.
4. Start the game once. The MSCLoader console and mod list should appear on the main menu, and `mysummercar_Data\Managed\MSCLoader.dll` should now exist. The C# mod build references that DLL.

## 2. Build tools

Run these from an elevated PowerShell. Every tool has a winget package.

```powershell
# Git (CommonLibSSE-NG is a submodule, and vcpkg fetches its registry with git)
winget install --id Git.Git -e

# Build Tools for VS 2022: MSVC, Windows SDK, CMake, Ninja and vcpkg. Compiler only, no IDE.
winget install --id Microsoft.VisualStudio.2022.BuildTools -e --override "--passive --wait --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended --add Microsoft.VisualStudio.Component.Vcpkg"

# .NET 8 SDK for the C# mod (it targets .NET Framework 3.5 through reference-assembly packages)
winget install --id Microsoft.DotNet.SDK.8 -e
```

Already have Build Tools? Open **Visual Studio Installer** > Build Tools 2022 > Modify, tick **Desktop development with C++**, and make sure "C++ CMake tools for Windows" and "vcpkg package manager" are ticked on the right.

You don't need to put CMake, Ninja or vcpkg on your PATH. `scripts/build.ps1` finds the Build Tools install with `vswhere`, enters its x64 developer shell, and uses the copies bundled there (that shell also sets `VCPKG_ROOT`).

<details>
<summary>Using a standalone vcpkg instead</summary>

```powershell
git clone https://github.com/microsoft/vcpkg C:\vcpkg
C:\vcpkg\bootstrap-vcpkg.bat
[Environment]::SetEnvironmentVariable('VCPKG_ROOT', 'C:\vcpkg', 'User')
```

`scripts/build.ps1` respects an existing `VCPKG_ROOT`.
</details>

## 3. Clone and configure

```powershell
git clone --recurse-submodules https://github.com/rackmarten/PerkeleRim.git
cd PerkeleRim
Copy-Item local.props.example local.props
```

Edit `local.props` (gitignored) and set:

| Property | Example |
|---|---|
| `SkyrimPath` | `C:\Program Files (x86)\Steam\steamapps\common\Skyrim Special Edition` |
| `MscPath` | `C:\Program Files (x86)\Steam\steamapps\common\My Summer Car` |
| `MscModsPath` | `<MscPath>\Mods` if you kept the default; otherwise `Documents\MySummerCar\Mods` or `AppData\LocalLow\Amistech\My Summer Car\Mods` |

If you cloned without `--recurse-submodules`, `scripts/build.ps1` initialises the submodule for you.

If PowerShell refuses to run the scripts, allow local scripts once for your user:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## 4. Build, deploy, check logs

```powershell
scripts/build.ps1                  # both halves, Release
scripts/build.ps1 -Target skse     # SKSE plugin only (-Configuration Debug for a debug build)
scripts/build.ps1 -Target msc      # MSCLoader mod only
scripts/test.ps1                   # unit tests: C++ protocol (doctest) and C# link (NUnit), no games needed
scripts/test.sh                    # the same on Linux, minus the shared memory link tests (see below)
scripts/deploy-local.ps1           # copy both DLLs (and PerkeleRim.esp once built) into the games (close the games first)
scripts/build-esp.ps1 -Deploy       # generate PerkeleRim.esp (garage, beer drinks, drunk blur) from your Skyrim.esm, copy and enable it (close Skyrim first)
scripts/logs.ps1                   # tail of the plugin, SKSE and My Summer Car logs
scripts/package.ps1                # player release zip (launcher/ + built files + launcher/saves/MSC and Skyrim if present) in tools/out/release/ (build and build-esp first)
scripts/starter-save.ps1           # copy your current MSC save into launcher/saves/MSC with thirst, hunger, urine, fatigue, stress, drunk and dirtiness at 0
scripts/starter-save.ps1 -Skyrim   # copy your newest Skyrim save (made before the first F10) into launcher/saves/Skyrim as PerkeleRim-Kumpo
scripts/launch-msc.ps1             # start MSC windowed at 640x480, lowest quality, single-threaded rendering, with no clicks (clicks Play! on the config dialog, presses past the news screen)
scripts/restart-games.ps1 -Build    # close both games, build, deploy, start MSC and Skyrim (SKSE), wait for "link up"
scripts/msc-save.ps1 snapshot NAME  # save MSC save data outside the repo; "restore NAME" puts it back
scripts/spike.ps1 -Command dump    # hidden-MSC spike tools, see docs/SPIKE_HIDDEN_MSC.md
scripts/skyrim-key.ps1 -Key F10    # press a key in Skyrim (brings it to the front); -Screenshot FILE saves its window
```

For a test loop with no clicks at all, create an empty `PerkeleRim_autoload.txt` in both game folders: MSC then continues its save, and Skyrim continues its most recent save from the main menu (the SKSE plugin does it once per start). Delete the files to get the main menus back.

From WSL, call the same scripts through Windows PowerShell, e.g. `powershell.exe -File scripts/build.ps1`.

On Linux (no Windows at all), `scripts/test.sh` runs the same unit tests natively, except the shared memory link's own tests (C++ `test_link.cpp`, C# `LinkTests`), which call Windows' API. It needs CMake 3.21+, Ninja, a C++23 compiler (GCC 13 or a recent Clang) and the .NET 8 SDK; the `linux-tests` CMake preset downloads doctest itself, so there is no vcpkg. `scripts/test.sh cpp` or `scripts/test.sh cs` runs one side. Nothing else builds on Linux.

The first SKSE build takes a while (often 15 to 25 minutes): vcpkg builds the dependencies and CommonLibSSE-NG compiles from source. Later builds are incremental. Outputs:

- `skse/build/release/PerkeleRim.dll`, deployed to `<SkyrimPath>\Data\SKSE\Plugins\`
- `msc/bin/Release/net35/PerkeleRim.dll`, deployed to `<MscModsPath>\`

### Hello-world check

1. Start Skyrim with `skse64_loader.exe`, wait for the main menu, quit.
2. Start My Summer Car, wait for the main menu (the MSCLoader console shows `PerkeleRim loaded`), quit.
3. Run `scripts/logs.ps1`. Expect:
   - in `PerkeleRim.log`: `PerkeleRim loaded (plugin 0.1.0, Skyrim runtime 1.7.104.0)`
   - in `skse64.log`: a line saying `PerkeleRim.dll` loaded correctly
   - in `output_log.txt` (My Summer Car game folder): `PerkeleRim: PerkeleRim loaded`

## 5. Debugging and inspection tools (optional)

- **Van NIF conversion** (`tools/convert-vehicle.ps1`, Blender, PyNifly, AssetStudioModCLI, texconv; see [tools/README.md](../tools/README.md)) writes the van as a NIF for NifSkope or Blender. Playing doesn't need it: the van comes from MSC's own model at runtime.

- **VS Code** (`winget install --id Microsoft.VisualStudioCode -e`) with the **C/C++** extension (`ms-vscode.cpptools`). Debug the SKSE plugin by attaching to `SkyrimSE.exe` with the `cppvsdbg` debugger type and a Debug build (`scripts/build.ps1 -Target skse -Configuration Debug`, then deploy it). The `.pdb` is deployed next to the DLL.
- **Visual Studio Community 2022** is a reasonable upgrade later if you want a full IDE debugger. It isn't needed for anything here.
- **dnSpyEx** (<https://github.com/dnSpyEx/dnSpy/releases>) to read My Summer Car's `Assembly-CSharp.dll` and MSCLoader. Use it for reading only: never commit decompiled game code.
- **Developer Toolset** (an MSCLoader mod on Nexus Mods) is the in-game object inspector for My Summer Car. It browses the scene hierarchy and components, and views and edits **PlayMaker FSM** states, variables and events, which is where most of My Summer Car's game logic lives. Generic Unity inspectors such as UnityExplorer need BepInEx or MelonLoader rather than MSCLoader, so they're not the default here.

## How the builds work

### SKSE plugin (`skse/`)

- CMake + Ninja with the MSVC toolset, C++23, dependencies from vcpkg in manifest mode (`skse/vcpkg.json`, pinned by the baseline in `skse/vcpkg-configuration.json`), triplet `x64-windows-static-md`.
- **CommonLib:** [`alandtse/CommonLibSSE-NG`](https://github.com/alandtse/CommonLibSSE-NG), the `ng` branch, as a git submodule at `skse/extern/CommonLibSSE-NG`, pinned to a release tag (currently `v10.1.0`). Why this fork:
  - The original CharmedBaryon CommonLibSSE-NG, which started the "NG" single-DLL approach, stopped tracking new runtimes. It has no 1.7.x support.
  - alandtse's fork is the maintained continuation. It regularly merges powerof3's CommonLibSSE (the most actively reverse-engineered tree), already supports runtimes 1.7.99 and 1.7.104, and is what large current plugins build on.
  - It builds one DLL for SE, AE and VR, uses Address Library, integrates directly with CMake (`add_commonlibsse_plugin`), and publishes prebuilt bundles that CI uses to skip the long CommonLib compile.
  - Licence: GPL-3.0-or-later with a modding exception (`skse/extern/CommonLibSSE-NG/EXCEPTIONS.md`). Our own source stays MIT, which is GPL-compatible. A distributed plugin binary links CommonLib, so ship it with a link to this repo's source.
- To bump CommonLib: `git -C skse/extern/CommonLibSSE-NG fetch --tags && git -C skse/extern/CommonLibSSE-NG checkout <tag>`, copy its `vcpkg-configuration.json` baseline into `skse/vcpkg-configuration.json`, add any new dependencies from its `vcpkg.json` to `skse/vcpkg.json`, then rebuild.

### MSCLoader mod (`msc/`)

- SDK-style project targeting `net35` (what My Summer Car's Unity 5 Mono runs), built with `dotnet build`. The `Microsoft.NETFramework.ReferenceAssemblies` NuGet package supplies the .NET 3.5 reference assemblies, so no Visual Studio or targeting pack install is needed.
- `MSCLoader.dll` and `UnityEngine.dll` are referenced from `<MscPath>\mysummercar_Data\Managed` (read from `local.props` through `msc/Directory.Build.props`) and never copied to the output or committed.
- `msc/stubs/MSCLoader` is a compile-only stand-in for `MSCLoader.dll` with just the API surface the mod uses. Build with `-UseStubs` (or `-p:UseStubs=true`) to compile without a game install. CI does this. When the mod starts using more MSCLoader API, add matching declarations to the stub, copying names and signatures from the real MSCLoader.

### CI (`.github/workflows/build.yml`)

Runs on GitHub-hosted runners for every push and pull request. A quick `ubuntu-latest` job runs `scripts/test.sh`. The `windows-latest` job uses the same `scripts/build.ps1` as local builds, caches vcpkg binaries, builds the C# mod against the stubs (the game's DLLs can't be redistributed), runs `scripts/test.ps1`, and uploads both DLLs as a build artifact.

## Troubleshooting

- **`... was unexpected at this time` when entering the developer shell:** a quote character in your `PATH` confuses `VsDevCmd.bat`. `scripts/build.ps1` strips quotes for its own process, but it's worth fixing the entry in System Properties > Environment Variables.
- **`MSCLoader.dll not found`:** MSCLoader isn't installed yet, or `MscPath` in `local.props` is wrong.
- **Skyrim crashes at start, or SKSE says the plugin is incompatible:** the game updated. Check the runtime version, then update SKSE and Address Library to match.
