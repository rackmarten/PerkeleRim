# PerkeleRim

> **Fus-Perkele-Dah!** A Finnish summer in Skyrim.

**My Summer Car** and **Skyrim Anniversary Edition** run at the same time. Skyrim owns the world and draws everything; My Summer Car runs hidden and simulates the player, the vehicles, the beer and the piss. Drive the Satsuma across the Whiterun tundra, get drunk, sneak by crouching, and take the Kekmet tractor up the Throat of the World.

Inspired by [SkyCraft](https://github.com/chasmlol/SkyCraft), which couples Minecraft and Skyrim the same way.

## Status

**Concept / pre-prototype.** Both halves build as hello-world stubs that only log that they loaded. Follow the [changelog](CHANGELOG.md) for progress.

## How it works

- An **SKSE plugin** (C++) samples Skyrim's collision around the player, mirrors NPCs, and renders the My Summer Car player, vehicles and items inside Skyrim.
- An **MSCLoader mod** (C#) builds that collision inside My Summer Car, runs the player and vehicle physics on it, and streams poses and events back.
- The two talk over versioned shared memory.

Details: [docs/DESIGN.md](docs/DESIGN.md) and [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Keys

F10 hands the player to My Summer Car and back. While MSC has the player you never need its window: WASD and the mouse are MSC's, Skyrim keeps Escape (pauses both games), Tab, M (map), F5/F9 and the console, and MSC's hand actions sit next to WASD:

| Key | Action |
|---|---|
| Left / right click (free hands) | punch / guard (Skyrim blocks) |
| V | middle finger |
| Z | Skyrim's shout or power, as usual, but Finn yells it (Unrelenting Force: "Perkele!") |
| Alt (hold) | piss |
| Middle click | swear |
| C / B | smoke / zoom |
| Ctrl / G / T (on foot) | crouch / push / hitchhike thumb |
| Shift / Ctrl (driving) | shift up / down |
| H (driving) | horn: people in the way run off |
| F8 (driving) | chase camera for filming, on / off (hides the HUD) |
| [ / ] | call the Jonnez moped / the Kekmet tractor: it's parked in front of you, wherever it was |
| Y, 3 to 8 | pocket (Skyrim's inventory); driving, 3 and 5 to 8 drink and eat |

Driving, G and T are MSC's shift up and boost, X its clutch. MSC's own keys (N, P, O, J, I, ...) still work. Full table for both games: [docs/PROTOCOL.md, Input](docs/PROTOCOL.md#input).

Tip: make your character a male Nord. While MSC has the player, Skyrim's own voice is muted and Finn does the yelling, grunting and swearing, so a Nord fits best whenever Skyrim has the player back.

## Requirements (planned)

- Windows 10/11
- [The Elder Scrolls V: Skyrim Anniversary Edition](https://store.steampowered.com/app/489830/) (latest Steam version), with SKSE64 and Address Library
- [My Summer Car](https://store.steampowered.com/app/516750/) (Steam) with MSCLoader
- A PC that can run both games at once

To build from source, see [docs/DEV_SETUP.md](docs/DEV_SETUP.md).

## Repository layout

| Path | Contents |
|---|---|
| [`docs/`](docs/) | Design and protocol documentation |
| [`protocol/`](protocol/) | Shared, versioned message layout (C header, mirrored in C#) |
| [`skse/`](skse/) | SKSE plugin (C++) |
| [`msc/`](msc/) | MSCLoader mod (C#) |
| [`skyrim-data/`](skyrim-data/) | Skyrim plugin (ESP), garage, scripts |
| [`tools/`](tools/) | ESP generator, optional dev tools (offline van NIF conversion) and debugging helpers |
| [`scripts/`](scripts/) | Build, local deploy and log scripts (PowerShell) |

## Game assets

This repository contains **no assets from My Summer Car or Skyrim**, and contributions must not add any. You need your own copies of both games. Skyrim draws MSC's van and items from MSC's own models, read from your running game at play time, so nothing is converted or installed beyond the release zip. See [CONTRIBUTING.md](CONTRIBUTING.md#game-assets).

## Contributing

Contributions are welcome once the first prototype is up. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md) first.

## License

[MIT](LICENSE) © Marek Uncik ([rackmarten](https://github.com/rackmarten)), except [`tools/esp-gen/`](tools/esp-gen/), a build-time tool that uses the GPL-3.0 Mutagen library and is GPL-3.0 itself ([its LICENSE](tools/esp-gen/LICENSE)). The ESP it generates contains no code from it.

Not affiliated with or endorsed by Amistech Games or Bethesda Softworks. My Summer Car and Skyrim are trademarks of their respective owners.
