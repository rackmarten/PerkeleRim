# tools

Development helpers. Everything they produce comes from your own game installs and must never be
committed: `tools/.deps/` (downloaded tools) and `tools/out/` (converted assets) are gitignored.

## Vehicle mesh conversion (optional)

Converts an MSC vehicle into a Skyrim SE NIF plus DDS textures, for looking at it in NifSkope or
Blender. **The game doesn't need it:** since protocol v27 MSC sends the van's own model at runtime
(`msc/src/Game/VehicleModel.cs`) with a node for every object that has a mesh, posed from MSC every
frame, and the SKSE plugin builds it in memory. It no longer loads `Meshes/PerkeleRim/Hayosiko.nif`,
and the part list below only matters to this converter.
Run it against your own My Summer Car install; the converted files are never shipped.

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

Each entry of `parts` (MSC hierarchy paths; protocol v26 and older posed exactly these from
`VehicleParts`) becomes a node named by its path, at the part's pivot, directly under the root; the
shapes below that path hang in it. `"lit": true` parts (lights, lamps)
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
`Textures/PerkeleRim/Hayosiko/*.dds`); `-Deploy` copies it into Skyrim's `Data` folder, where nothing
loads it any more.

`tools/out/hayosiko/raw/` keeps the intermediate `model.json` and PNGs for debugging.

## Garage ESP

`esp-gen/` (C#, .NET 8, [Mutagen](https://github.com/Mutagen-Modding/Mutagen) 0.53) writes
`PerkeleRim.esp`: the garage near Whiterun built from vanilla pieces, with a pit and shelves, the
shack next to it, a spawn marker in front of it and a marker where the van parks. It reads only your own `Skyrim.esm` (base objects, the Tamriel cell
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
- The pit: four `Stonewall01` (two per side, x -216..216), each side topped by a flat `RTDockRamp01`
  plank track 63 units above the highest ground under the shed. The tracks are 60 units apart, so
  the van stands on them and Finn crouches in the gap under it (the walls' tops are rounded, too
  far apart on their own: the van fell between them); two more planks tilted up from the front. The shed is raised by the
  same 63 units so the van on the pit clears its beams.
- `CommonShelf01` (parts), `CommonShelf03` and `CommonShelf04` along the back wall; the last two get
  three spot markers each on their top board (`PerkeleRimShelfSpot0..5`, listed in that order in
  the FormList `PerkeleRimShelfSpots`, `0x910`), where MSC puts its store items.
- The shack on the garage's right side (-Y): two ShackKit tiles in a row with plank walls, a window,
  a doorway in the +X end (the way the garage opens), a gable roof, and a `CommonBed01` in the back
  tile.
- Two persistent `XMarkerHeading`s in Tamriel's persistent cell at fixed local form ids the SKSE
  plugin looks up: `0x800` the spawn point 620 units in front of the opening, facing in (only used
  with an ESP from before the shack), and `0x801` the van's spot in the middle on top of the pit,
  nose out.
- `0x80B`, the persistent marker next to the bed facing the doorway, where the first handover of a
  save wakes Finn up. Pieces added later get ids after the existing ones, so older saves keep
  theirs.

To move or change the garage, edit those constants and rebuild. The pieces were picked by rendering
the vanilla meshes in Blender; the spot comes from the terrain heights (flat to about 50 units) and
from keeping clear of roads, fences and farms.
