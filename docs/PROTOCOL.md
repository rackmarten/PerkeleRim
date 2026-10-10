# Protocol

> Status: protocol v11. Transport implemented (shared memory, handshake, slots, rings, coordinate conversion); input and player pose live (milestone 1a); collision chunks streamed (milestone 1b); one vehicle pose (milestone 2a); the van parked at a given spot (milestone 2c); the van's doors, controls and lights (milestone 2d); MSC's camera pose and look-at prompt (v6, v7); the scroll wheel, MSC's needs and subtitles (v8); mead picked up in Skyrim as MSC beer (v9); MSC's own models built in Skyrim (v10); MSC's physics items drawn in Skyrim (v11). Message contents beyond `InputState`, `PlayerState`, `VehicleState`, `VehicleParts`, `ItemState`, `CollisionChunk`, `WorldspaceChange`, `LookAtText`, `Subtitle`, `GiveBeer` and `ModelChunk` arrive with the milestones that need them.

The SKSE plugin (Skyrim side) and the MSCLoader mod (My Summer Car side) exchange data over one shared memory section. See [DESIGN.md](DESIGN.md) for the overall architecture and ownership rules.

| File | What it is |
|---|---|
| [`protocol/perkelerim_protocol.h`](../protocol/perkelerim_protocol.h) | Source of truth: every struct, offset and constant, with `static_assert`s |
| [`protocol/cpp/link.h`](../protocol/cpp/link.h), `link.cpp` | C++ end of the link (used by `skse/`, tested standalone) |
| [`protocol/cpp/coords.h`](../protocol/cpp/coords.h) | C++ coordinate conversion |
| [`msc/src/Link/`](../msc/src/Link) | C# mirror: `Protocol.cs` (structs), `Link.cs`, `Coords.cs`. Unity-free |
| `protocol/tests/`, `msc/tests/` | C++ (doctest) and C# (NUnit) tests, run by `scripts/test.ps1` and CI |

## Section layout

One section, `Local\PerkeleRim.Link`, 4,463,872 bytes. Whichever game starts first creates it; it disappears when both have closed.

| Offset | Size | Part | Written by |
|---|---|---|---|
| 0 | 128 | `LinkHeader`: magic, sizes, creator version, one `PeerInfo` per side | creator, then each side its own `PeerInfo` |
| 128 | 128 | `InputSlot` (latest `InputState`) | Skyrim |
| 256 | 128 | `PlayerSlot` (latest `PlayerState`) | MSC |
| 384 | 128 | `VehicleSlot` (latest `VehicleState`) | MSC |
| 512 | 1600 | `VehiclePartsSlot` (latest `VehicleParts`) | MSC |
| 2112 | 5184 | `ItemSlot` (latest `ItemState`) | MSC |
| 7296 | 64 + 4 MiB | ring Skyrim → MSC (collision chunks, worldspace changes) | Skyrim produces, MSC consumes |
| 4,201,664 | 64 + 256 KiB | ring MSC → Skyrim (look-at text, later events) | MSC produces, Skyrim consumes |

Layout rules:

- Everything is packed (`#pragma pack(1)` / `StructLayout(Sequential, Pack = 1)`), little-endian and fixed size. C# field names match the header.
- `LinkHeader` is frozen across versions. Any other layout change bumps `kProtocolVersion`.
- The C++ header `static_assert`s every size and offset. The C# tests parse those asserts and the `k*` constants out of the header and check the C# mirror against them, so the two can't drift silently.

## Handshake and liveness

Both sides run the same state machine (`Detached` → `Waiting` → `Up`, or `Disabled`), ticked 10+ times a second. Skyrim ticks it on its own thread at 20 Hz, so it also runs in menus. MSC ticks it from `Update`, so the heartbeat stops whenever MSC's main loop stalls.

1. Create or open the section. A fresh section gets its header filled in and the magic written last; a joining side waits while the magic is still 0.
2. Unknown magic or header size: the link is disabled (`not a PerkeleRim link`).
3. Each side publishes its `PeerInfo` (version, pid), even when the versions differ, so both sides can report a mismatch. If the creator's version, or the peer's, differs from ours, both log `protocol mismatch: ... speaks vN, this ... speaks vM` and stay disabled until restarted.
4. On attach, a side throws away whatever is left in its inbound ring (sent to an earlier instance of itself).
5. Each tick bumps our heartbeat. The link is **up** while the peer's heartbeat keeps moving. The first read only seeds the value, so a crashed peer never looks alive. It goes **down** after 2 s without a heartbeat change (e.g. MSC loading a scene) or at once when the peer detaches cleanly (pid set to 0).

Log lines on both sides: `created shared memory` / `joined shared memory`, `link up: <peer> pid N, protocol vN`, `link down: ...`, `link disabled: ...`. The Skyrim side logs to `PerkeleRim.log` (prefixed `link:`), the MSC side to the MSCLoader console and `output_log.txt` (prefixed `PerkeleRim:`).

## Latest-value slots

Continuous state (`InputState`, `PlayerState`, `VehicleState`) goes through a seqlock slot, not a queue: the reader only ever wants the newest value, and a queue would add lag. The writer bumps `seq` to odd, writes, then bumps it back to even. The reader retries if `seq` changed or was odd. `seq == 0` means never written. Reads return nothing while the link is down.

## Rings

One single-producer, single-consumer byte ring per direction, for messages that must all arrive (collision chunks, events). `head` (producer) and `tail` (consumer) are monotonic byte counts on separate halves of the ring header. A record is an 8-byte `MessageHeader` (`type`, `size`) plus payload and may wrap around the end. `Send` refuses while the link is down, when the ring is full, or when the message can never fit.

| Direction | Message | Contents |
|---|---|---|
| Skyrim → MSC | `CollisionChunk` | `CollisionChunkHeader` (chunk x/y, worldspace, generation, origin, counts), vertices relative to origin, triangle indices |
| Skyrim → MSC | `WorldspaceChange` | worldspace, interior cell, generation, spawn position and heading; sent on every handover and whenever the game moves the player (load door, fast travel), as the island anchor. With `flags & kAnchorVehicleSpot`, `vehicleSpot` and `vehicleHeading` say where the van parks for this anchor (the garage on the first handover of a save) instead of next to the player |
| Skyrim → MSC | `GiveBeer` | `count` bottles of beer for MSC: the player picked up that much mead or ale in Skyrim (the drinks in PerkeleRim.esp's `PerkeleRimBeerDrinks` list), and Skyrim took it out of the inventory once this was sent. MSC adds the bottles to the nearest beer case of the player's with room (bottles it drank), else spawns a new case next to the player with just that many bottles. Beer that arrives while Skyrim has the player waits for the next handover |
| MSC → Skyrim | `LookAtText` | MSC's interaction prompt for what its crosshair is on: its text (global `GUIinteraction`: "IGNITION", item names) and, with `flags & kLookAtUse`, its hand icon for things to use (global `GUIuse`: doors, hatches). `LookAtText` header (`length`, at most `kLookAtTextMax` = 255, `flags`) then UTF-8 bytes; no text and no flags clears it. Sent only when it changes, and from scratch each time MSC gets the player. Encoded by `msc/src/Link/LookAt.cs`, decoded by `protocol/cpp/look_at.h` |
| MSC → Skyrim | `ModelChunk` | Part of one of MSC's own models, read from the running game (see [Models](#models)): `ModelChunk` header (`model` id, `totalSize`, `offset`, `size` up to `kModelChunkMax` = 48 KiB) then that slice of the model blob. Chunks of a model arrive in order from offset 0 |
| MSC → Skyrim | `Subtitle` | MSC's subtitle line (global `GUIsubtitle`: the player's swearing and other speech). Same payload as `LookAtText` with flags 0; no text clears it. Sent only when it changes, and from scratch each time MSC gets the player |

Planned, added with their milestones (each one bumps the version): `ActorList`, `PlayerDamage`, `ConsumeEvent` (Skyrim → MSC); `ImpactEvent`, `PunchEvent`, `UrineHit`, `Death` (MSC → Skyrim).

Input pass-through keys (activate, shout, journal/map) stay with Skyrim. E is also MSC's lean right, so it does both for now. Q (MSC's lean left) would open Skyrim's favorites and pause the game, so `FavoritesHandler::CanProcess` refuses while MSC owns the player. Ctrl (MSC's zoom) is Skyrim's sneak, which is off while handed over anyway.

## Models

MSC sends its own meshes and textures and Skyrim builds them in memory: nothing is converted offline or written to disk, so the mod ships none of MSC's assets. A model is a set of active `MeshRenderer`s below one MSC object (for items: see [Items](#items)), as one blob (`ModelHeader` with name and counts; per texture a `ModelTexture` header and RGBA8 pixels, top row first; per submesh a `ModelMesh` header with its path below the object, texture index, `ModelVertex` position/normal/UV and 16-bit triangle indices). Positions are in Skyrim axes and units relative to the object's position and rotation, with its scale applied, front faces counterclockwise seen from the front, UV (0, 0) top left. The blob is cut into `ModelChunk` messages so it fits the 256 KiB ring whatever its size; Skyrim's `Model::Assembler` (`protocol/cpp/model.h`) puts it back together and `Model::Parse` checks every bound. Encoded by `msc/src/Link/Model.cs`.

MSC (`msc/src/Game/ModelSender.cs`) reads textures by drawing them into a RenderTexture and reading that back at the end of a frame into an `ARGB32` texture (MSC's textures have no CPU copy, and Unity 5.0's `ReadPixels` refuses `RGBA32`); larger than 1024 is halved. Skyrim (`skse/src/model/ModelView.cpp`) makes D3D11 vertex and index buffers and a mipmapped texture itself, wraps them in a `BSTriShape` cloned from a vanilla mesh and a `BSLightingShaderProperty` with Skyrim's flat default normal map, under one `NiNode` in the player's scene node, so the world lights it. The beer case with its bottles (25 submeshes, 1794 vertices, two 512 x 512 textures) builds in about 5 ms with no measurable frame cost.

A model's id (`ModelChunk.model`, never 0) stands for what it is made of: MSC keys it by the meshes and textures (by name, size and bounds, since every spawned copy of an item has its own mesh instance) and their placement, so identical items share one id and one model. MSC sends each model once per link session and everything again after the link comes back up; its ids start at a random base, so a restarted MSC never reuses an id a running Skyrim still has. Skyrim keeps every model it got by id (a resend replaces it) and builds a fresh node from it per item. Hand-built textures, the temporary materials and item nodes are never freed: the game's destructors expect objects it built itself, so item nodes are kept per model and reused. Spike command `textures <name>` reads any object's model and logs it without sending it.

## Items

`ItemState` (MSC → Skyrim, latest-value slot): MSC's frame, the anchor `generation`, `count` and up to `kItemCapacity` (128) `ItemPose`s, closest to the player first: MSC's item `id` (stable while the item exists, never 0), the `model` id of its look (0 = nothing to draw), `flags` (`kItemVisible` while MSC draws it, `kItemHeld` while it is in the player's hands) and its root's position and rotation (quaternion) in Skyrim space. An item missing from one frame to the next is gone. Only written with items while MSC has the player on the current island; otherwise `count` is 0.

MSC side (`msc/src/Game/ItemLink.cs`): every 0.5 s it looks for `Rigidbody` objects within 15 m of the player's feet (kept until 20 m) that are MSC items: named `...(itemx)` or under `ITEMS`, or any non-kinematic loose rigidbody up to 30 kg that isn't part of the player or of something 100 kg or heavier (a thrown bottle; not the van's doors). Each item is split: its root's own renderers are one entry, and each direct child with renderers is an entry of its own, posed on its own. A beer case's bottles are its children, so a drunk bottle simply disappears, and all bottles share one model. Poses go out every frame; a destroyed piece drops out the same frame. When a new anchor moves the island under the player (the next handover, a load door), the items are moved by the same offset so each stays where it was in Skyrim's world, as long as that is within 20 m of the player; the rest stay behind. MSC itself does all the handling (picking up, carrying, dropping, throwing, drinking); mouse and keys already reach it. Spike command `items` logs every entry with its path and model id.

Skyrim side (`skse/src/item/ItemView.cpp`): one node per item id, made by `ModelView::Instance` once its model has arrived, hung under the player's parent scene node like the van (no reference, no collision), posed every frame and app-culled while not visible. Items that go away, change model, or all of them on F10 or an anchor change go back to a per-model pool for reuse.

## Collision chunks

Chunks are `kCollisionChunkUnits` (2048 units, about 29 m) squares on a grid anchored at world (0, 0); chunk `(x, y)` starts at `(x * 2048, y * 2048)`. A payload is the header, `vertexCount` vertices (`float[3]`, Skyrim units relative to `origin`) and `indexCount` triangle indices. Triangles wind counterclockwise seen from above in Skyrim; MSC swaps two corners per triangle because the Y/Z swap flips the handedness. A chunk with the same x/y and generation replaces the older one; chunks for another generation are dropped.

Skyrim side (`skse/src/collision/CollisionSampler.cpp`, geometry in `protocol/cpp/collision.h`): while MSC owns the player, a heightfield of 33 x 33 samples (64 units apart) per chunk, from top-down Havok raycasts that keep terrain, statics, animated statics, stair helpers and tree trunks and pass through everything else (clutter, actors, debris, water). So it is the top surface only: no overhangs, walls become steep slopes. A miss leaves a hole (no triangles touching it). The 5 x 5 chunks around the player are sampled nearest first, then the 5 x 5 around a point 2 s of travel ahead of it (at most 2 chunks ahead, so it only matters while driving), about 2 ms per frame; a chunk with holes is sampled again every 2 s (up to 5 tries) and resent when it improves. Skyrim forgets chunks more than 3 away from both centers and MSC drops them more than 6 away from the player, so a chunk MSC dropped is always resent.

MSC side (`msc/src/Link/CollisionChunk.cs`, `msc/src/Game/PlayerLink.cs`): one `MeshCollider` per chunk under `PerkeleRim.Island`. Chunks copy the layer, surface tag and physics material of MSC's asphalt (`Terrain` layer, tag `Concrete`, material `Track`): vehicle wheels only see some layers and take their grip from the surface. A new anchor clears them; the player is held at the anchor with its motor off until a chunk under it has arrived, then put down on it.

Log lines: `collision: chunk (x,y) gen N try T: hits/1089 hit (terrain, static, trees, passed through), ms over frames` in `PerkeleRim.log`; `player: chunk (x,y) gen N: ... new|rebuilt`, `player: dropped N far chunks`, `player: placed on the island for gen N after S s` in MSC's log.

## Vehicles

`VehicleState` (MSC → Skyrim, latest-value slot): vehicle id (`kVehicleHayosiko`), flags (`kVehicleValid` while it is on the current anchor's island, `kVehicleDriven` while the player sits in it), anchor `generation`, position of the vehicle's origin, rotation as a quaternion in Skyrim space (local +Y forward, +Z up), velocity, front wheel steer angle (radians, positive turns right) and each wheel's spin angle about its axle (radians in [-π, π], positive rolls forward), which Skyrim applies to the van mesh's `Wheel*` and `Wheel*:Spin` nodes. One vehicle for now.

`VehicleParts` (MSC → Skyrim, latest-value slot, written right after `VehicleState`): vehicle id, anchor `generation`, `count` and up to `kVehiclePartCapacity` (48) `VehiclePart`s: rotation (quaternion) and pivot translation relative to the vehicle's body in Skyrim axes and units, and `kPartVisible` while MSC draws the part. Part `i` is entry `i` of the vehicle's fixed list, `kHayosikoParts` in the header: MSC hierarchy paths below the van's root (doors, steering wheel, key, gauge needles, pedals, levers, wipers, fuel hatch and cap, radio tuner, seatbelt, and the lit meshes: beams, brake, reverse, blinkers, gauge backlight and dashboard lamps). The C# mirror (`Wire.HayosikoParts`) and `tools/nif/hayosiko.json` list the same paths, and `msc/tests` checks all three agree. Changing the list bumps the version. A part's pose is relative to the body, not to its MSC parent, so nesting doesn't matter on the Skyrim side.

MSC side (`msc/src/Game/VehicleLink.cs`, one instance per vehicle): when the player is put down on a new anchor on foot, the van is parked to its right and the Jonnez moped to its left, each at the first spot on its own side (usual, 4 m ahead, 4 m back, 3 m further out) where the ground under its center and corners is within 0.4 m, dropped above the highest point (a rock under one corner used to throw it, sometimes into the player); a vehicle the player sits in is moved to the new anchor with the player in it and set down on its wheels at the ground there. Both get their key (`PlayerKeyHayosiko`, as MSC's debug "Keys" command does) and a full tank. A vehicle with no island ground under it is made kinematic and keeps its velocity until the ground arrives. Only the van has a wire id, so only it is sent. Every frame it also reads each listed part's transform relative to the van's root and whether it is drawn (object active and renderer, if any, enabled): MSC switches lights and lamps by turning objects or renderers off.

Skyrim side (`skse/src/vehicle/VehicleView.cpp`): the van mesh converted from the player's own MSC install (`Meshes\PerkeleRim\Hayosiko.nif`, see [tools/README.md](../tools/README.md)), or a vanilla cart stand-in (`Furniture\Cart\CartFurnStatic01.nif`) when it wasn't converted, loaded through `BSModelDB` and hung under the player's parent node, posed every frame from a valid `VehicleState` of the current generation. While the player drives it (`skse/src/player/PlayerSync.cpp`), it is hung around Skyrim's camera instead of at MSC's pose: Skyrim builds the camera before the `PlayerCharacter::Update` hook from the previous frame's placement and smooths small moves, so the van jittered and fell behind at speed. The camera's spot in the van is learned while the van is below 0.5 m/s and kept for the session; the player's character controller isn't simulated while seated (like a rider). It has no reference and no Havok collision, so the collision sampler never sees it. The converted mesh has one node per listed part, named by its path; VehicleView sets each from `VehicleParts` (same vehicle and generation) and app-culls the ones MSC doesn't draw. Lit parts use Skyrim's glow shader, so they glow while shown.

Log lines: `vehicle: found/wheel models/N/40 parts/parked/got in/got out` and `surface: ...` (MSC's ground surfaces, once per game load) in MSC's log; `vehicle view: model ..., N of 4 wheel nodes, N of 40 part nodes, showing/hidden` and `player sync: ... seated=true van shift=D` (how far the van was moved off MSC's pose to stay around the camera) in `PerkeleRim.log`.

## Input

MSC runs hidden while Skyrim has focus, so it can't read the keyboard and mouse itself. Skyrim captures input and writes `InputState`. The MSC mod redirects every `UnityEngine.Input` call in the game's assemblies (cInput included) to a shim and answers from the latest `InputState`. `Input.mousePosition` answers with the screen center (MSC aims its look-at raycasts through the cursor, which isn't locked while its window has no focus). Details and measurements: [SPIKE_HIDDEN_MSC.md](SPIKE_HIDDEN_MSC.md).

- `keys`: 512-bit set of held Unity `KeyCode`s (mouse buttons are `Mouse0`..`Mouse6`). The shim derives Down/Up edges per MSC frame.
- `mouseX`, `mouseY`: the `Mouse X`/`Mouse Y` axes **accumulated** since Skyrim started. The two games run at different frame rates, so MSC applies the difference since its last frame; a per-frame delta would be lost or doubled.
- `mouseWheel`: the `Mouse ScrollWheel` axis, accumulated the same way (`kWheelAxisPerNotch`, 0.1 per notch, Unity's default). MSC uses the wheel for the radio dial, among others.
- `horizontal`, `vertical`: the Unity axes, -1..1.
- `flags & kInputOwnsPlayer`: MSC should apply this input at all.

Skyrim side (`protocol/cpp/input.h`, `skse/src/player/PlayerSync.cpp`): DirectInput scan codes and mouse buttons map to Unity `KeyCode`s; Escape, Tab, `~` and F10 never reach MSC. Mouse counts become axis units (`kMouseAxisPerCount`, 0.06 per count, Y flipped). The wheel arrives as mouse buttons 8 (up) and 9 (down), one press per notch; Skyrim's own wheel zoom is off while MSC owns the player. While a menu pauses the game, keys and axes are sent empty so MSC stands still. Every frame, keys and mouse buttons the devices no longer report as down are released, in case a release event got lost. MSC side (`msc/src/Link/InputFrame.cs`, `msc/src/Game/PlayerLink.cs`): the shim pulls the newest `InputState` before the first `Input` call of each MSC frame; the first state after a handover only seeds the mouse totals.

## Handing over the player

F10 in Skyrim toggles who owns the player. The first F10 of a save (when `PerkeleRim.esp` is loaded) first moves the player to the garage spawn marker; once its cell is attached and the player stands there, the handover below runs with `kAnchorVehicleSpot` and the van marker as `vehicleSpot`, so MSC parks the van inside the garage instead of next to the player. The SKSE cosave remembers this per save. Taking over:

1. Skyrim sends `WorldspaceChange` with the player's position and heading as `spawn` (a new `generation` each time), then publishes `InputState` with `kInputOwnsPlayer` and turns off its own movement, look, sneak, jump, attack and POV controls.
2. MSC drains its ring before writing a pose, so the anchor is always applied first. The anchor maps to `Island.Base` on a collision island far from MSC's map, built from `CollisionChunk`s (see above). MSC holds the player's feet at the anchor until the ground there arrives, then puts it down.
3. Every frame MSC writes `PlayerState` (feet position in Skyrim world space, velocity, heading, camera pitch, stance from the `Crouch` FSM, and the anchor `generation`). `kPlayerValid` is set only while MSC owns the player and stands on the current anchor's island. With `kPlayerCamera` it also carries MSC's rendering camera: `eye` (camera minus feet, Skyrim axes and units), `roll` (positive tilts the view right) and `fov` (vertical degrees), so leaning (Q/E), reaching for the door while seated, crouching and the Ctrl zoom all show. With `kPlayerNeeds` it carries MSC's needs in `kNeed*` order: thirst, hunger, stress, urine, fatigue, dirtiness (globals `Player*`, 0..100) and drunk level (`PlayerDrunk`, 0..8).
4. Skyrim's `PlayerCharacter::Update` hook places the player at every valid pose whose `generation` is the current one (position, heading, pitch, zeroed controller velocity) and sets sneak while the stance is crouch or prone. Skyrim has already built its first-person camera by then (from the previous frame's placement, pivoting around the head), so the hook then overrides the camera node: placed feet plus `eye`, turned to the pose's heading, pitch and `roll` (`protocol/cpp/camera.h` keeps whatever axes Skyrim's node has on top), and sets `worldFOV` from `fov` (converted to Skyrim's horizontal-for-4:3 value; the old value comes back on release). A driven van is hung around the camera minus `eye`, and its seat is only learned while the view is level against the van (MSC moves the seated player when it leans), so leaning moves the view inside the van.
5. Skyrim draws its own HUD layer while MSC owns the player (`skse/src/hud`): a small dot at the screen center (Skyrim's crosshair is hidden then) and the latest `LookAtText` below it: a hand icon with `kLookAtUse`, the text on a dark box. MSC's needs as bars bottom left above the magicka bar, like MSC's own HUD (urine always yellow, any bar 80 % full turns red, urine orange; the drunk bar is full at level 4), and the latest `Subtitle` on a dark box near the bottom center.
   MSC's drunk level also drives a blur with double vision (the `PerkeleRimDrunkIMAD` imagespace modifier from PerkeleRim.esp, looped), its strength the drunk level / 4, so it grows and fades with MSC's own and is full where the drunk bar is.
6. If the game moves the player itself while MSC owns it (another worldspace or interior cell, or a jump of more than 2048 units: load doors, fast travel, scripts), Skyrim sends a new anchor and the island is rebuilt there.

F10 again, or the link going down, hands the player back to Skyrim and turns its controls back on.

## Coordinates

On the wire everything is in **Skyrim world space**. MSC converts on its side and adds the collision island offset.

- Skyrim: X east, Y north, Z up (right-handed), game units. Heading is the Z angle in radians, 0 = north, clockwise seen from above. Pitch positive looks down.
- Unity: X east, Y up, Z north (left-handed), meters. Yaw is `eulerAngles.y` in degrees, same zero and direction.
- Scale: 1 unit = 0.0142875 m, Havok's world scale in Skyrim (69.99 units per meter).
- Positions map axis by axis (Y and Z swap, which also flips the handedness), so angles keep their sign. Results are normalized to [0, 360) degrees or [0, 2π) radians.
- One function per direction on each side (`coords.h`, `Coords.cs`), both checked against the same vectors in `protocol/tests/coords_vectors.csv`.
