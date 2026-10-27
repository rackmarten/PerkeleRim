# PerkeleRim: design

> A Finnish summer in Skyrim. **My Summer Car** and **Skyrim Special Edition** run at the same time: Skyrim owns the world and draws everything, while My Summer Car (MSC) runs hidden and simulates the player, the vehicles, the beer and the piss.

Author: Marek Uncik (rackmarten) · Status: concept / pre-prototype · Target game versions: Skyrim Anniversary Edition (latest, Steam), My Summer Car (latest, Steam) · Target: personal build + YouTube video

---

## 1. Inspiration

[SkyCraft](https://github.com/chasmlol/SkyCraft) couples Minecraft and Skyrim SE: Skyrim keeps its world, NPCs and saves; a hidden Minecraft instance simulates the player; an SKSE plugin and a Fabric mod talk over shared memory, and Skyrim's collision is fed into Minecraft's collision.

This project uses the same pattern with **MSC in Minecraft's role**. Read SkyCraft's `docs/DESIGN.md` and `protocol/skycraft_protocol.h` before writing code.

## 2. Why My Summer Car

- Cheap, cult classic, large and active modding community (MSCLoader).
- Built around bumpy gravel roads and forests, so its vehicles fit Skyrim's terrain.
- Unity + Mono (not IL2CPP): easy to patch with Harmony, and **runtime collision is trivial** (`Mesh` + `MeshCollider`).
- Rarely updated, so hooks don't break often.
- Has a first-person walking character with real interactions (drinking, pissing, crouching, picking up and throwing items), not just a car.

Alternatives considered: Motor Town (best trucks/drift, but early access and hard runtime collision), BeamNG (soft-body damage hates rough terrain, CPU-heavy), GTA V (popular, but no clean runtime mesh collision), Assetto Corsa (static baked tracks), MudRunner/SnowRunner/ETS2 (closed, no runtime modding).

## 3. Goals

- Spawn at a **garage near Whiterun** (flat tundra between the Whiterun stables and Pelagia Farm) with a **pre-built car**.
- MSC character walks, crouches and drives in Skyrim.
- **Crouch = Skyrim sneak** (detection + skill training).
- MSC emotes, **beer drinking** and drunk effects.
- **Pissing on Jarl Balgruuf** causes a bounty.
- Enter towns on foot, talk to NPCs, **accept and complete simple quests**.
- **Damage both ways**: enemies hurt the player; fists, thrown bottles and vehicles hurt NPCs.
- Punch bandits and **dragons**; run people over (Skyrim ragdolls).
- **Climb the Throat of the World in the Kekmet tractor.**
- Overall vibe: an actual Finnish summer in Skyrim.

## 4. Non-goals (for now)

- Delivery jobs / trucking economy (future project).
- Multiplayer of any kind.
- Using Skyrim inventory, barter or magic menus while MSC controls the player.
- Third-person MSC body (first person only at first).
- Public release with redistributed game assets (see §10).

## 5. Architecture

```
┌──────────────────────────────┐        shared memory        ┌──────────────────────────────┐
│   Skyrim SE (visible)        │ ◄─────────────────────────► │   My Summer Car (hidden)     │
│                              │                             │                              │
│  SKSE plugin (C++)           │  terrain + static collision │  MSCLoader mod (C#)          │
│  - samples collision around  │ ──────────────────────────► │  - builds MeshCollider island│
│    the player                │  NPC capsules + poses       │  - mirrors NPCs as capsules  │
│  - mirrors NPCs              │ ──────────────────────────► │  - runs player, vehicles,    │
│  - camera, rendering of MSC  │                             │    items, needs, drunkenness │
│    player/vehicles/items     │  player + vehicle poses     │  - reads PlayMaker FSM state │
│  - crime, damage, quests     │ ◄────────────────────────── │                              │
│                              │  events (hit, piss, punch)  │                              │
│                              │ ◄────────────────────────── │                              │
│                              │  damage, death, drink       │                              │
│                              │ ──────────────────────────► │                              │
└──────────────────────────────┘                             └──────────────────────────────┘
```

### Ownership rules

| Thing | Authority |
|---|---|
| World, NPCs, quests, saves, weather, time | Skyrim |
| Player movement, crouch, vehicle physics, physics items | MSC |
| Player health | Skyrim (MSC's player never dies while handed over; its deaths cost Skyrim health) |
| Drunkenness, hunger, thirst, urine, fatigue | MSC |
| Money | Skyrim (its gold; MSC's `PlayerMoney` follows both ways) |
| Crime / bounty | Skyrim (native crime system) |
| Rendering | Skyrim |

### Collision island

- The SKSE side samples terrain **and static object collision** (rocks, bridges, walls, the 7,000 Steps) in a radius around the player.
- The MSC side builds `MeshCollider` chunks on an island far away from MSC's own map and moves the player, vehicles and items there.
- Chunks rebuild as the player moves; filter out small clutter (grass, small rocks, fences) to keep driving smooth. While driving, chunks ahead of the player are streamed too.
- Chunks copy MSC's asphalt surface (layer, tag, physics material), which is where vehicle wheels take their grip from.
- On worldspace or interior change (load doors), the island is rebuilt for the new space.

### Coordinates

- Skyrim: Z-up, game units (1 unit = 0.0142875 m, Havok's scale; about 70 per meter). Unity: Y-up, meters, left-handed.
- On the wire everything is in Skyrim world space; MSC converts and adds the island offset.
- Keep one conversion function per direction, unit-tested.

### Running MSC hidden

Proven by the hidden-MSC spike ([SPIKE_HIDDEN_MSC.md](SPIKE_HIDDEN_MSC.md)):

- MSC runs windowed at a small resolution, unfocused or minimized; the mod forces `Application.runInBackground` and physics keeps a steady 50 Hz.
- MSC can't read the keyboard and mouse while Skyrim has focus, so input is forwarded from Skyrim over the protocol and fed in by redirecting MSC's `UnityEngine.Input` calls to a shim (see [PROTOCOL.md](PROTOCOL.md#input)).
- Its logic lives mostly in **PlayMaker FSMs**; the ones we need are mapped in [FSM_MAP.md](FSM_MAP.md).
- On the Skyrim side the player is placed every frame from the MSC pose in a `PlayerCharacter::Update` hook (position, heading, zeroed controller velocity); the native controller doesn't fight it.

## 6. Protocol

One shared memory section: a versioned, packed layout in a C++ header, mirrored in C# with `StructLayout`. Latest-value slots for continuous state (input, player pose), one ring per direction for messages that must all arrive. Layout, handshake and input forwarding: see [PROTOCOL.md](PROTOCOL.md).

## 7. Feature mapping

| Feature | Implementation |
|---|---|
| Garage near Whiterun | Generated ESP (`tools/esp-gen`, Mutagen) with an open shed and plank walls from vanilla pieces, a pit on a low plinth the van parks on (stone walls, plank ramps), shelves at the back, spawn point in front, van marker on the pit. |
| Play mode | Loading a save hands the player to MSC by itself once the game has settled and MSC is in its game; without MSC Skyrim says so and waits for it. F10 is a debug toggle. See PROTOCOL.md, Handing over the player. |
| Helgen intro | A new game is the intro: Kumpo drank one beer too many in MSC and wakes up in Skyrim's own Helgen cart. On the black opening MSC plays him snoring, then his hangover line (`hangover05`); after Ralof's "Hey, you. You're finally awake." he says Perkele (`swear09`). Skyrim fades up from black again and captions each line. Helgen plays with Skyrim's controls; the handover comes once he's out of the keep's cave. The shack's wake-up on a save's first handover plays the same hangover line. |
| Finn's home | A plank shack with a usable bed next to the garage, in the exterior worldspace (no load screen). The first handover of a save (one that didn't start in the Helgen cart) wakes Finn up by the bed with Skyrim's fade up from black; the van waits on the pit. The first handover near the garage has MSC stock the shelves once per save (beer cases and food from MSC's store spawner, `StockShelves`); the SKSE cosave remembers both. |
| Pre-built car | Save with a finished Satsuma, or start with Hayosiko van / Gifu / Kekmet, which are drivable from the start. |
| Beer and emotes | MSC simulates; Skyrim draws arms/particles; drunk level drives Skyrim image-space effects (Skooma-style blur). |
| Crouch = sneak | MSC crouch → Skyrim player sneak state. |
| Piss on Jarl → bounty | Skyrim traces the urine arc; on NPC hit, send an assault alarm credited to the player. |
| Talking / quests | Activate key → Skyrim; dialogue, doors and quest pickups work natively. Quest items go into Skyrim's inventory through the activate pass-through. |
| Damage to player | Skyrim health is authoritative. MSC's player can't die while handed over: a crash MSC would die of costs half of Skyrim's max health, falls cost Skyrim's vanilla fall damage. At 0, MSC's blood over the view and its death sound, then Skyrim's own death and reload. MSC's alcohol, cigarettes and food heal a share of it. |
| Punching | MSC's own punch (Hit key, or left click with free hands) on a capsule of a Skyrim actor; Skyrim hits it as the player (Skyrim's unarmed damage scaled up, more when drunk, stagger). See PROTOCOL.md, Combat. |
| Thrown bottles | An item MSC's player throws passes through an actor's capsule → `ImpactEvent` → Skyrim hits it like a punch, by speed. Bottles fly twice as fast, as a ranged attack. Holding the right button charges the throw like a bow: the item is pulled back, a tap throws half MSC's distance and a second twice (speed x sqrt of that). Piss is poison: the stream on a capsule → `ImpactEvent` → a damage over time. The driven vehicle passes through capsules and runs them over: `ImpactEvent` → knocked over, damage by speed, lethal from 50 km/h. See PROTOCOL.md, Combat. |
| Running people over | Capsule hit → impulse + damage on the real NPC; Skyrim ragdoll does the rest. |
| Cover behind vehicles | Each drawn vehicle gets keyframed Havok capsules in Skyrim's world, sized from MSC's model of it, that stop projectiles and actors but not the player or line of sight; out of the world at run-over speed. See PROTOCOL.md, Vehicle hitbox. |
| Finnish summer | Force clear weather, long bright evenings; maybe a sauna in the garage. |

## 8. Target quests

| Quest | Why it fits |
|---|---|
| Before the Storm | Travel + dialogue at Dragonsreach. |
| Dragon Rising | Kill Mirmulnir at the western watchtower with fists, bottles and the Satsuma. |
| The Way of the Voice | Kekmet tractor up the Throat of the World to High Hrothgar (shout key passes to Skyrim). |
| Small bounties / bandit camps | Combat via punching and running over. |

## 9. Known limitations

- Vehicles can't go through load doors: park at city gates; the player walks in on foot.
- The island has the land as a heightfield (0.9 m apart) and statics as their real collision; spheres and transform wrappers that don't read as a rotation are left out (counted in the log as skipped), and tree trunks are 8-sided prisms. Doors, gates and trapdoors are read again only once they have stopped moving (about 0.5 s after), so you can't slip through a double door while it is still opening as you can in Skyrim.
- While MSC controls the player, Skyrim's inventory (Tab) works for armor, food and drink, but weapons and spells can't be equipped, and the Favorites menu opens with R on foot (Q is MSC's lean).
- MSC is first-person only; third-person would need a body model and animation.
- MSC vehicles are fragile and can kill the player in crashes; may need patching for Skyrim terrain.
- Two games running at once: expect high CPU/GPU load.

## 10. Assets and legal

- MSC's models (the van, items, overlays) are read at runtime from the player's own running MSC and built in Skyrim's memory (protocol `ModelChunk`): nothing is converted, shipped or written to disk, so a player needs only SKSE, Address Library, MSCLoader and the release zip.
- `tools/convert-vehicle.ps1` (offline NIF conversion of the van from your own install) stays as an optional dev tool for inspecting meshes; the game doesn't use its output.
- Do **not** distribute converted MSC or Skyrim assets.

## 11. Milestones

1. **Plumbing**: SKSE terrain sampler → shared memory → MSCLoader mod builds collider; MSC player walks on Skyrim ground; Skyrim camera follows.
2. **Satsuma on the tundra**: drivable near Whiterun, drawn in Skyrim. First filmable moment.
3. **Body and booze**: beer, drunk effects, crouch-sneak, damage both ways.
4. **Talk and crime**: activate pass-through, Jarl dialogue, piss-bounty scene.
5. **Combat**: punching, bottles, running over bandits, Mirmulnir.
6. **Finale**: Kekmet tractor up the Throat of the World.
7. *(Future)* Deliveries, traffic, trucking.
8. *(Future)* Doors that update as they move: read a moving door's chunks while it swings, not only once it has stopped, so the player can run through a half-open double door as in Skyrim (costs MSC a collider rebuild every quarter second on big rooms). Left to player feedback.

## 12. Risks

| Risk | Mitigation |
|---|---|
| Rendering MSC objects in Skyrim | Start with offline NIF conversion; runtime extraction later. |
| MSC pausing or ignoring input while hidden | Prove it in an early spike before building on it; force `runInBackground`, feed input through the protocol. |
| PlayMaker FSM complexity | Map required FSMs early; isolate FSM access behind one adapter. |
| Collision quality (snags, falling through) | Stream generously around the player; prioritize terrain + large statics; filter clutter. |
| Worldspace/interior transitions | Explicit rebuild step with player frozen until the island is ready. |
| Game updates breaking hooks | Pin Skyrim version; log clear version-mismatch errors. |
| Performance with two games | MSC at minimum settings/resolution; limit chunk radius. |

## 13. Repo layout (proposal)

```
PerkeleRim/
├── docs/
│   ├── DESIGN.md
│   └── PROTOCOL.md
├── protocol/
│   ├── perkelerim_protocol.h # shared struct layout, versioned
│   ├── cpp/                  # C++ link + coordinate conversion
│   └── tests/                # C++ tests, shared conversion vectors
├── skse/                     # C++ SKSE plugin (CommonLibSSE-NG)
├── msc/                      # C# MSCLoader mod
├── skyrim-data/              # ESP, garage, scripts
└── tools/                    # mesh conversion, debugging helpers
```

## 14. Open questions

- Best way to draw MSC arms and items in Skyrim's first-person view.
- How to trigger MSC emotes from a cleaner input layer instead of raw keys.
- Garage spot: (15170, -14516), flat tundra south-west of the Whiterun stables, open toward 31.7 degrees east of north; picked in game (the first pick at the carriage stop sat on the road). See tools/README.md to move it.

## 15. References

- SkyCraft: https://github.com/chasmlol/SkyCraft
- SKSE / CommonLibSSE-NG / Address Library (Skyrim SE plugin stack)
- MSCLoader (My Summer Car mod loader)
- PlayMaker FSM viewers for Unity mods
