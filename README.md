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
| Z | Skyrim's shout or power, as usual, but Kumpo yells it (Unrelenting Force: "Perkele!") |
| Alt (hold) | piss |
| Middle click | swear |
| C / B | smoke / zoom |
| Ctrl / G (on foot) | crouch / push stance on / off (walk into people to shove them aside; rights a rolled van) |
| O | hitchhike thumb |
| T (on foot) | Skyrim's Wait menu: Kumpo waits or sleeps, MSC's clock and needs go along (fatigue half as fast without a bed) |
| Shift / Ctrl (driving) | shift up / down |
| H (driving) | horn: people in the way run off |
| F8 (driving) | chase camera for filming, on / off (hides the HUD) |
| F11 | screenshot of both games, to Screenshots next to Start PerkeleRim.bat (the launcher sets this) |
| [ / ] / ; (on foot) | call the Jonnez moped / the Hayosiko van / the Kekmet tractor: it's parked in front of you, wherever it was |
| Y, 3 to 8 | pocket (Skyrim's inventory, never runs out of beer, bottles, food or cigarettes); driving, 4 to 8 drink and eat |

Driving, G and T are MSC's shift up and boost, X its clutch. MSC's own keys (N, P, O, J, I, ...) still work. Kumpo fights with his fists: weapons and spells can't be equipped while MSC has the player, armor works as usual, and the drunker he is, the harder he hits and the more he can take. A punch also cuts spider webs and breaks destructibles in front of him (oil pots, barricades), and so does a thrown bottle that hits one. A throw from sneak at someone who has not noticed Kumpo is a sneak attack for double damage. Sitting in the Hayosiko van or the Kekmet is safe: nothing in Skyrim can hurt Kumpo there (the moped is no shelter, and a crash still hurts). Full table for both games: [docs/PROTOCOL.md, Input](docs/PROTOCOL.md#input).

Tip: make your character a male Nord. While MSC has the player, Skyrim's own voice is muted and Kumpo Kempolaari (Finn) does the yelling, grunting and swearing, so a Nord fits best whenever Skyrim has the player back.

## Install and play

You need:

- Windows 10/11 and a PC that can run both games at once
- [The Elder Scrolls V: Skyrim Anniversary Edition](https://store.steampowered.com/app/489830/) (latest Steam version) with [SKSE64](https://skse.silverlock.org) and [Address Library for SKSE Plugins](https://www.nexusmods.com/skyrimspecialedition/mods/32444)
- [My Summer Car](https://store.steampowered.com/app/516750/) (Steam) with [MSCLoader](https://www.nexusmods.com/mysummercar/mods/147)

Then:

1. Extract the release zip anywhere (not inside a game folder).
2. Double-click **Start PerkeleRim.bat**. Every time, it:
   - finds both games in your Steam libraries, or asks for their folders once (saved in `PerkeleRim.ini` next to it; delete that file to look again),
   - tells you if SKSE, Address Library or MSCLoader is missing,
   - installs or updates the mod (the SKSE plugin and `PerkeleRim.esp` in Skyrim, enabled in `plugins.txt`; the MSCLoader mod where MSCLoader keeps mods),
   - gives My Summer Car a starter save (needs reset to fresh) when you have none, never over yours,
   - starts My Summer Car in a small window that minimizes itself and continues your MSC save, then starts Skyrim through SKSE.
3. Play in Skyrim only. Load a save (or start a new game) and press **F10** to hand over to My Summer Car. Leave MSC minimized.

To build from source, see [docs/DEV_SETUP.md](docs/DEV_SETUP.md); `scripts/package.ps1` builds the release zip.

## Reporting bugs

Double-click **Report a bug.bat** (next to Start PerkeleRim.bat). It puts `PerkeleRim-bug-<date>.zip` in the `Bug reports` folder next to the launcher, with your PerkeleRim logs from both games, `PerkeleRim.ini` and the game versions (your Windows user name is replaced by `%USERPROFILE%`), then opens the bug form on GitHub with the versions filled in. Say what happened and drag the zip into the form. Nothing is sent anywhere until you submit it, and PerkeleRim has no telemetry of any kind.

## Known issues

- Followers get in the way and are easy to run over.
- Water is ignored: Kumpo walks on the bottom, Skyrim can drown him, vehicles drive through water, and there is no thirst or washing.
- Vehicles have no hitbox in Skyrim, so arrows and dragons pass through the van. Kumpo is safe inside the van and the Kekmet anyway.

## Repository layout

| Path | Contents |
|---|---|
| [`docs/`](docs/) | Design and protocol documentation |
| [`protocol/`](protocol/) | Shared, versioned message layout (C header, mirrored in C#) |
| [`skse/`](skse/) | SKSE plugin (C++) |
| [`msc/`](msc/) | MSCLoader mod (C#) |
| [`skyrim-data/`](skyrim-data/) | Skyrim plugin (ESP), garage, scripts |
| [`tools/`](tools/) | ESP generator, optional dev tools (offline van NIF conversion) and debugging helpers |
| [`launcher/`](launcher/) | Player launcher shipped in the release zip, starter saves (`launcher/saves/`) |
| [`scripts/`](scripts/) | Build, local deploy, packaging and log scripts (PowerShell) |

## Game assets

This repository contains **no assets from My Summer Car or Skyrim**, and contributions must not add any. You need your own copies of both games. Skyrim draws MSC's van and items from MSC's own models, read from your running game at play time, so nothing is converted or installed beyond the release zip. See [CONTRIBUTING.md](CONTRIBUTING.md#game-assets).

## Contributing

Contributions are welcome once the first prototype is up. Read [CONTRIBUTING.md](CONTRIBUTING.md) and the [Code of Conduct](CODE_OF_CONDUCT.md) first.

## License

[MIT](LICENSE) © Marek Uncik ([rackmarten](https://github.com/rackmarten)), except [`tools/esp-gen/`](tools/esp-gen/), a build-time tool that uses the GPL-3.0 Mutagen library and is GPL-3.0 itself ([its LICENSE](tools/esp-gen/LICENSE)). The ESP it generates contains no code from it.

Not affiliated with or endorsed by Amistech Games or Bethesda Softworks. My Summer Car and Skyrim are trademarks of their respective owners.
