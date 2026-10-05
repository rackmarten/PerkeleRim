# Protocol

> Status: v1 transport implemented (shared memory, handshake, slots, rings, coordinate conversion); input and player pose live (milestone 1a). Message contents beyond `InputState`, `PlayerState`, `CollisionChunk` and `WorldspaceChange` arrive with the milestones that need them.

The SKSE plugin (Skyrim side) and the MSCLoader mod (My Summer Car side) exchange data over one shared memory section. See [DESIGN.md](DESIGN.md) for the overall architecture and ownership rules.

| File | What it is |
|---|---|
| [`protocol/perkelerim_protocol.h`](../protocol/perkelerim_protocol.h) | Source of truth: every struct, offset and constant, with `static_assert`s |
| [`protocol/cpp/link.h`](../protocol/cpp/link.h), `link.cpp` | C++ end of the link (used by `skse/`, tested standalone) |
| [`protocol/cpp/coords.h`](../protocol/cpp/coords.h) | C++ coordinate conversion |
| [`msc/src/Link/`](../msc/src/Link) | C# mirror: `Protocol.cs` (structs), `Link.cs`, `Coords.cs`. Unity-free |
| `protocol/tests/`, `msc/tests/` | C++ (doctest) and C# (NUnit) tests, run by `scripts/test.ps1` and CI |

## Section layout

One section, `Local\PerkeleRim.Link`, 4,456,896 bytes. Whichever game starts first creates it; it disappears when both have closed.

| Offset | Size | Part | Written by |
|---|---|---|---|
| 0 | 128 | `LinkHeader`: magic, sizes, creator version, one `PeerInfo` per side | creator, then each side its own `PeerInfo` |
| 128 | 128 | `InputSlot` (latest `InputState`) | Skyrim |
| 256 | 64 | `PlayerSlot` (latest `PlayerState`) | MSC |
| 320 | 64 + 4 MiB | ring Skyrim → MSC (collision chunks, worldspace changes) | Skyrim produces, MSC consumes |
| 4,194,688 | 64 + 256 KiB | ring MSC → Skyrim (events) | MSC produces, Skyrim consumes |

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

Continuous state (`InputState`, `PlayerState`) goes through a seqlock slot, not a queue: the reader only ever wants the newest value, and a queue would add lag. The writer bumps `seq` to odd, writes, then bumps it back to even. The reader retries if `seq` changed or was odd. `seq == 0` means never written. Reads return nothing while the link is down.

## Rings

One single-producer, single-consumer byte ring per direction, for messages that must all arrive (collision chunks, events). `head` (producer) and `tail` (consumer) are monotonic byte counts on separate halves of the ring header. A record is an 8-byte `MessageHeader` (`type`, `size`) plus payload and may wrap around the end. `Send` refuses while the link is down, when the ring is full, or when the message can never fit.

| Direction | Message | Contents |
|---|---|---|
| Skyrim → MSC | `CollisionChunk` | `CollisionChunkHeader` (chunk x/y, worldspace, generation, origin, counts), vertices relative to origin, triangle indices |
| Skyrim → MSC | `WorldspaceChange` | worldspace, interior cell, generation, spawn position and heading; also sent on every handover as the island anchor |

Planned, added with their milestones (each one bumps the version): `ActorList`, `PlayerDamage`, `ConsumeEvent` (Skyrim → MSC); `VehicleState`, `ItemState`, `ImpactEvent`, `PunchEvent`, `UrineHit`, `Death` (MSC → Skyrim).

Input pass-through keys (activate, shout, journal/map) stay with Skyrim.

## Input

MSC runs hidden while Skyrim has focus, so it can't read the keyboard and mouse itself. Skyrim captures input and writes `InputState`. The MSC mod redirects every `UnityEngine.Input` call in the game's assemblies (cInput included) to a shim and answers from the latest `InputState`. Details and measurements: [SPIKE_HIDDEN_MSC.md](SPIKE_HIDDEN_MSC.md).

- `keys`: 512-bit set of held Unity `KeyCode`s (mouse buttons are `Mouse0`..`Mouse6`). The shim derives Down/Up edges per MSC frame.
- `mouseX`, `mouseY`: the `Mouse X`/`Mouse Y` axes **accumulated** since Skyrim started. The two games run at different frame rates, so MSC applies the difference since its last frame; a per-frame delta would be lost or doubled.
- `horizontal`, `vertical`: the Unity axes, -1..1.
- `flags & kInputOwnsPlayer`: MSC should apply this input at all.

Skyrim side (`protocol/cpp/input.h`, `skse/src/player/PlayerSync.cpp`): DirectInput scan codes and mouse buttons map to Unity `KeyCode`s; Escape, Tab, `~` and F10 never reach MSC. Mouse counts become axis units (`kMouseAxisPerCount`, 0.06 per count, Y flipped). While a menu pauses the game, keys and axes are sent empty so MSC stands still. MSC side (`msc/src/Link/InputFrame.cs`, `msc/src/Game/PlayerLink.cs`): the shim pulls the newest `InputState` before the first `Input` call of each MSC frame; the first state after a handover only seeds the mouse totals.

## Handing over the player

F10 in Skyrim toggles who owns the player. Taking over:

1. Skyrim sends `WorldspaceChange` with the player's position and heading as `spawn` (a new `generation` each time), then publishes `InputState` with `kInputOwnsPlayer` and turns off its own movement, look, sneak, jump, attack and POV controls.
2. MSC drains its ring before writing a pose, so the anchor is always applied first. The anchor maps to `Island.Base` on a collision island far from MSC's map (milestone 1a: a flat 2 km placeholder MeshCollider), and MSC teleports the player's feet there.
3. Every frame MSC writes `PlayerState` (feet position in Skyrim world space, velocity, heading, camera pitch, stance from the `Crouch` FSM). `kPlayerValid` is set only while MSC owns the player and stands on the current anchor's island, so Skyrim never applies a pose from an earlier handover.
4. Skyrim's `PlayerCharacter::Update` hook places the player at every valid pose (position, heading, pitch, zeroed controller velocity) and sets sneak while the stance is crouch or prone.

F10 again, or the link going down, hands the player back to Skyrim and turns its controls back on.

## Coordinates

On the wire everything is in **Skyrim world space**. MSC converts on its side and adds the collision island offset.

- Skyrim: X east, Y north, Z up (right-handed), game units. Heading is the Z angle in radians, 0 = north, clockwise seen from above. Pitch positive looks down.
- Unity: X east, Y up, Z north (left-handed), meters. Yaw is `eulerAngles.y` in degrees, same zero and direction.
- Scale: 1 unit = 0.0142875 m, Havok's world scale in Skyrim (69.99 units per meter).
- Positions map axis by axis (Y and Z swap, which also flips the handedness), so angles keep their sign. Results are normalized to [0, 360) degrees or [0, 2π) radians.
- One function per direction on each side (`coords.h`, `Coords.cs`), both checked against the same vectors in `protocol/tests/coords_vectors.csv`.
