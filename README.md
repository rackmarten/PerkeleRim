# SkyrimSummerCar

> A Finnish summer in Skyrim.

**My Summer Car** and **Skyrim Anniversary Edition** run at the same time. Skyrim owns the world and draws everything; My Summer Car runs hidden and simulates the player, the vehicles, the beer and the piss. Drive the Satsuma across the Whiterun tundra, get drunk, sneak by crouching, and take the Kekmet tractor up the Throat of the World.

Inspired by [SkyCraft](https://github.com/chasmlol/SkyCraft), which couples Minecraft and Skyrim the same way.

## Status

**Concept / pre-prototype.** There is no working build yet. Follow the [changelog](CHANGELOG.md) for progress.

## How it works

- An **SKSE plugin** (C++) samples Skyrim's collision around the player, mirrors NPCs, and renders the My Summer Car player, vehicles and items inside Skyrim.
- An **MSCLoader mod** (C#) builds that collision inside My Summer Car, runs the player and vehicle physics on it, and streams poses and events back.
- The two talk over versioned shared memory.

Details: [docs/DESIGN.md](docs/DESIGN.md) and [docs/PROTOCOL.md](docs/PROTOCOL.md).

## Requirements (planned)

- Windows 10/11
- [The Elder Scrolls V: Skyrim Anniversary Edition](https://store.steampowered.com/app/489830/) (latest Steam version), with SKSE64 and Address Library
- [My Summer Car](https://store.steampowered.com/app/516750/) (Steam) with MSCLoader
- A PC that can run both games at once

Installation and build instructions will land with the first working build.

## Repository layout

| Path | Contents |
|---|---|
| [`docs/`](docs/) | Design and protocol documentation |
| [`protocol/`](protocol/) | Shared, versioned message layout (C header, mirrored in C#) |
| [`skse/`](skse/) | SKSE plugin (C++) |
| [`msc/`](msc/) | MSCLoader mod (C#) |
| [`skyrim-data/`](skyrim-data/) | Skyrim plugin (ESP), garage, scripts |
| [`tools/`](tools/) | Mesh conversion and debugging helpers |

## Game assets

This repository contains **no assets from My Summer Car or Skyrim**, and contributions must not add any. You need your own copies of both games. See [CONTRIBUTING.md](CONTRIBUTING.md#game-assets).

## Contributing

Contributions are welcome once the first prototype is up. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md) first.

## License

[MIT](LICENSE) © Marek Uncik ([rackmarten](https://github.com/rackmarten))

Not affiliated with or endorsed by Amistech Games or Bethesda Softworks. My Summer Car and Skyrim are trademarks of their respective owners.
