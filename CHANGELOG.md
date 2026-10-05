# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Repository scaffold: README, MIT license, contributing guide, code of conduct, security policy, issue and PR templates.
- Design document ([docs/DESIGN.md](docs/DESIGN.md)) and protocol proposal ([docs/PROTOCOL.md](docs/PROTOCOL.md)).
- Windows development setup guide ([docs/DEV_SETUP.md](docs/DEV_SETUP.md)) and `scripts/build.ps1`, `deploy-local.ps1`, `logs.ps1`.
- Hello-world SKSE plugin (CMake, vcpkg, CommonLibSSE-NG) and MSCLoader mod (`net35`, `dotnet build`) that log when they load.
- GitHub Actions build on `windows-latest`; the C# mod compiles against MSCLoader API stubs there.
- Hidden-MSC spike ([docs/SPIKE_HIDDEN_MSC.md](docs/SPIKE_HIDDEN_MSC.md)): MSC input redirect and FSM dump tools, Skyrim per-frame player placement test, PlayMaker FSM map ([docs/FSM_MAP.md](docs/FSM_MAP.md)).
- Dev loop scripts: `scripts/launch-msc.ps1` (no-click MSC start), `msc-save.ps1` (save snapshots), `spike.ps1` (commands and input scripts for the running game).
- `scripts/restart-games.ps1`: closes both games, optionally builds, deploys, starts both and waits for `link up`.

- Protocol v1 link ([docs/PROTOCOL.md](docs/PROTOCOL.md)): shared memory section with version handshake and heartbeat, latest-value slots for input and player state, one ring per direction, Skyrim <-> Unity coordinate conversion. C++ in `protocol/` (used by the SKSE plugin), C# mirror in `msc/src/Link`. Both games log `link up` when running together.
- Unit tests for both ends (`scripts/test.ps1`, also run in CI): C++ doctest, C# NUnit, layout checked against the C++ header's asserts, shared conversion vectors.
- Milestone 1a, live player loop: F10 in Skyrim hands the player to MSC. Skyrim forwards keyboard and mouse as `InputState` (scan codes mapped to Unity key codes, Escape/Tab/~ stay with Skyrim) and turns off its own movement, look, sneak, jump, attack and POV controls; MSC builds a flat placeholder collision island, plays the player on it from that input and reports `PlayerState`, and Skyrim places its player there every frame. MSC's Escape menu closes itself while handed over, MSC crouch/prone becomes Skyrim sneak, and MSC movement speeds are scaled by 2.75 while handed over (Harmony prefix on the character motor, with ground acceleration raised to match). Unit tests for the key mapping, input decoding and island math.
- Milestone 1b, real Skyrim ground: while MSC owns the player, the SKSE plugin samples Skyrim's collision around it (top-down Havok raycasts on a 64-unit grid, 5 x 5 chunks of 2048 units, terrain, statics and tree trunks kept, clutter, actors and water passed through) and streams `CollisionChunk` heightfields; MSC builds a MeshCollider per chunk on the island, replacing the flat placeholder, and drops chunks the player walked away from. Chunks with holes are sampled again. A new anchor (handover, load door, fast travel) clears the island and MSC holds the player until the ground under it has arrived. Unit tests for chunk building and parsing.

- Milestone 2a, driving on Skyrim ground: on every handover MSC parks the Hayosiko van to the player's right and the Jonnez moped to the left, on the collision island, with keys and full tanks. The van is reported to Skyrim as `VehicleState` and drawn there with a vanilla cart mesh as a stand-in (no collision). A vehicle the player sits in comes along to a new anchor; one with no island ground under it is held until the ground arrives. Island chunks copy MSC's asphalt (layer, surface tag and physics material), so the wheels grip. While the player moves fast, Skyrim also samples collision up to 2 s ahead of it. MSC logs its ground surfaces once per game load.

### Changed

- Protocol v3: `VehicleSlot` (latest `VehicleState`, MSC -> Skyrim) after the player slot, which moves both rings. Quaternion conversion between Unity and Skyrim space, with shared test vectors. Both halves must be updated together.

- Protocol v2: `PlayerState.generation` (the anchor a pose belongs to; Skyrim ignores poses for older anchors) and `kCollisionChunkUnits`. Both halves must be updated together.

- `scripts/launch-msc.ps1` keeps watching for Unity's configuration dialog until the game window appears (it can take over 10 s on a cold start).

- Project renamed from SkyrimSummerCar to PerkeleRim (tagline "Fus-Perkele-Dah!").

### Fixed

- MSC's look-at interactions (door handles, handbrake, ignition) aimed about 100 px off while handed over: `Input.mousePosition` now answers with the screen center.
- Skyrim could stay in sneak after MSC stood up again; the plugin now tracks the sneak state it sent instead of trusting Skyrim's flag.
- Keys or mouse buttons whose release event got lost no longer stay held for MSC: Skyrim checks the devices every frame and releases them.
