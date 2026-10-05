# tools

Development helpers. Everything they produce comes from your own game installs and must never be
committed: `tools/.deps/` (downloaded tools) and `tools/out/` (converted assets) are gitignored.

## Vehicle mesh conversion

Converts an MSC vehicle into a Skyrim SE NIF plus DDS textures, so Skyrim draws the real van instead
of the vanilla cart stand-in. Each player runs this against their own My Summer Car install; the
converted files are never shipped.

Pipeline (`convert-vehicle.ps1`):

1. `unity-export/` (a small C# tool built on AssetStudioMod's libraries) reads `mysummercar_Data`,
   finds the vehicle's root GameObject and writes `tools/out/<vehicle>/raw/model.json` (the object
   tree in raw Unity space: transforms, meshes, materials) plus its textures as PNG.
   It doesn't use AssetStudioModCLI's own FBX export: that crashes on MSC's Unity 5.0 animation
   clips, and the FBX it writes sends Blender's importer into endless recursion.
2. `texconv` turns the PNGs into BC3 DDS with mipmaps.
3. Blender (headless) runs `nif/build_nif.py` with PyNifly: it converts Unity space (Y up, meters,
   left-handed) to Skyrim space (Z up, 1 unit = 0.0142875 m) and writes the NIF.

Which parts go in, and how wheels are named, lives in `nif/<vehicle>.json` (`hayosiko.json` for the
van: skipped parts and materials, alpha-blended materials, wheel nodes, moving parts).

The NIF has the body's shapes under the root, and per wheel a `WheelFL`/`WheelFR`/`WheelRL`/`WheelRR`
node at the wheel center with a `<name>:Spin` child holding the tire and rim. The SKSE plugin steers
the front wheel nodes about Z and spins the `:Spin` nodes about X from `VehicleState`.

Each entry of `parts` (MSC hierarchy paths, the same list and order as `kHayosikoParts` in
`protocol/perkelerim_protocol.h`, checked by `msc/tests`) becomes a node named by its path, at the
part's pivot, directly under the root; the shapes below that path hang in it. The SKSE plugin poses
these nodes and hides switched-off ones from `VehicleParts`. `"lit": true` parts (lights, lamps)
are kept even where `skip` matches and use Skyrim's glow shader with their lit texture as glow map.

### Setup (once)

1. Install Blender 4.0 or newer: `winget install BlenderFoundation.Blender` (or set `BlenderPath` in
   `local.props` to a `blender.exe` elsewhere).
2. Download the rest into `tools/.deps/` (AssetStudioModCLI, PyNifly, texconv; pinned versions):

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools/setup.ps1
   ```

   PyNifly is loaded from there by the build script; it isn't installed into your Blender profile.
3. .NET SDK 8 (already needed for the MSC mod) builds `unity-export` on the first run.

### Run

```powershell
powershell -ExecutionPolicy Bypass -File tools/convert-vehicle.ps1 -Deploy
```

Takes about 15 seconds. Output lands in `tools/out/hayosiko/Data/` (`Meshes/PerkeleRim/Hayosiko.nif`,
`Textures/PerkeleRim/Hayosiko/*.dds`); `-Deploy` copies it into Skyrim's `Data` folder. Without the
converted mesh the SKSE plugin falls back to the cart stand-in.

`tools/out/hayosiko/raw/` keeps the intermediate `model.json` and PNGs for debugging.

## Garage ESP

`esp-gen/` (C#, .NET 8, [Mutagen](https://github.com/Mutagen-Modding/Mutagen) 0.53) writes
`PerkeleRim.esp`: the garage near Whiterun built from vanilla pieces, a spawn marker in front of it
and a marker where the van parks. It reads only your own `Skyrim.esm` (base objects, the Tamriel cell
to add to, terrain height there) and writes the same bytes every time. **This folder is GPL-3.0**
(Mutagen is), unlike the rest of the repository; see `esp-gen/LICENSE`.

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-esp.ps1 -Deploy
```

Output goes to `tools/out/esp/PerkeleRim.esp`; `-Deploy` copies it into Skyrim's `Data` folder and
adds `*PerkeleRim.esp` to `%LOCALAPPDATA%/Skyrim Special Edition/plugins.txt`. Close Skyrim first.

What it places (layout constants at the top of `esp-gen/Program.cs`):

- `SFarmShed01` (Solitude farm pole shed, 720 x 496 units) centered at (15170, -14516) in Tamriel
  cell (3, -4), open end facing 31.7 degrees east of north, raised 10 units above the highest
  ground under it so the van clears the beams.
- Eight `ShackWall01/02/05` plank walls closing the back and both long sides.
- Two persistent `XMarkerHeading`s in Tamriel's persistent cell at fixed local form ids the SKSE
  plugin looks up: `0x800` the spawn point 620 units in front of the opening, facing in, and
  `0x801` the van's spot in the middle, nose out.

To move or change the garage, edit those constants and rebuild. The pieces were picked by rendering
the vanilla meshes in Blender; the spot comes from the terrain heights (flat to about 50 units) and
from keeping clear of roads, fences and farms.
