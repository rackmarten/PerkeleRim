# Protocol

> Status: proposal. Nothing is implemented yet. The source of truth will be `protocol/ssc_protocol.h` once it exists; this doc explains it.

The SKSE plugin (Skyrim side) and the MSCLoader mod (My Summer Car side) exchange messages over shared memory. See [DESIGN.md](DESIGN.md) for the overall architecture and ownership rules.

## Messages

| Direction | Message | Contents |
|---|---|---|
| Skyrim → MSC | `InputState` | movement, look, action buttons (MSC has no focus, see [Input](#input)) |
| Skyrim → MSC | `CollisionChunk` | chunk id, origin, vertex/index buffers |
| Skyrim → MSC | `ActorList` | NPC ids, positions, capsule sizes, hostile flag |
| Skyrim → MSC | `PlayerDamage` | amount, source, knockback |
| Skyrim → MSC | `ConsumeEvent` | e.g. drank Nord mead → counts as beer |
| Skyrim → MSC | `WorldspaceChange` | new worldspace/cell, spawn transform |
| MSC → Skyrim | `PlayerState` | position, rotation, crouch, in-vehicle id, emote state, drunk level |
| MSC → Skyrim | `VehicleState` | vehicle id, transform, wheel spin and steer angles |
| MSC → Skyrim | `ItemState` | dynamic items (bottles, cases): id, transform |
| MSC → Skyrim | `ImpactEvent` | target actor id, speed, direction (vehicle or thrown item) |
| MSC → Skyrim | `PunchEvent` | target actor id, direction |
| MSC → Skyrim | `UrineHit` | target actor id |
| MSC → Skyrim | `Death` | cause |

Input pass-through keys (like SkyCraft's G/J/M): activate → Skyrim, shout → Skyrim, journal/map → Skyrim.

## Layout rules

- One versioned C header, `protocol/ssc_protocol.h`, mirrored in C# with `StructLayout(LayoutKind.Sequential, Pack = 1)`.
- The header starts with a magic value and a protocol version. A version mismatch logs a clear error on both sides and disables the link.
- Struct sizes and field offsets are asserted by tests on both sides, so the C++ and C# layouts can't drift silently.
- One ring buffer per direction.

## Input

MSC runs hidden while Skyrim has focus, so MSC can't read the keyboard and mouse itself. Input is captured on the Skyrim side and forwarded to MSC (an `InputState` message). The MSC mod redirects every `UnityEngine.Input` call in the game's assemblies (cInput included) to a shim and answers from the latest `InputState`, so `InputState` carries raw key states (as Unity `KeyCode`s), the `Mouse X`/`Mouse Y` axes and the `Horizontal`/`Vertical` axes. Details and measurements: [SPIKE_HIDDEN_MSC.md](SPIKE_HIDDEN_MSC.md).

## Coordinates

- Skyrim: Z-up, game units (roughly 70 units per meter; exact Havok scale to be confirmed).
- Unity: Y-up, meters, left-handed.
- One conversion function per direction on each side, unit-tested.
