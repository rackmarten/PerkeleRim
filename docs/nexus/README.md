# Nexus Mods page

The PerkeleRim page on Nexus Mods lives under **My Summer Car**. The page text is
kept here so that each release only needs a version bump, not a rewrite.

- `description.bbcode`: paste into the **Description** field as is (Nexus uses BBCode). Its images load from the GitHub repo's `main` branch, so they keep working after the page is published.
- `scripts/nexus-package.ps1` copies the release zip and the screenshots into `tools/out/nexus/` for uploading.

## Fields, in the order the Nexus "Add a mod" form asks for them

### 1. Mod details

| Field | Value |
|---|---|
| Game | My Summer Car |
| Mod name | PerkeleRim |
| Language | English |
| Version | 0.1.1 |
| Category | Gameplay (or the closest gameplay/overhaul category the form offers) |
| Author | rackmarten |
| Brief overview | see below |
| Description | contents of `description.bbcode` |
| Adult content | No |

Brief overview (220 characters, under Nexus's limit):

```
My Summer Car and Skyrim AE run at the same time: drive the Hayosiko van across the Whiterun tundra, get drunk on mead, punch a Thalmor and take the Kekmet up the Throat of the World. Open source, no game assets shipped.
```

### 2. Media

- **Primary image:** `trailer.jpg` (1280x720, the trailer thumbnail).
- **Other images:** `kekmet-aurora.jpg`, `van-break.jpg`, `thalmor-chase.jpg`, `rolled-van.jpg`, `kekmet-crash.jpg`.
- **Header image** (optional): skip for now. Nexus wants a wide banner, and the logo would be stretched.
- **Videos tab:** add `https://youtu.be/IJzDb4E7bYA`.

### 3. Files

| Field | Value |
|---|---|
| File | `PerkeleRim-0.1.1.zip` |
| File name | PerkeleRim |
| Version | 0.1.1 |
| Category | Main files |
| Mod manager download button | Off (the launcher installs it, Vortex can't) |
| File description | `The launcher, the SKSE plugin, PerkeleRim.esp, the MSCLoader mod and the starter saves. Extract anywhere and run Start PerkeleRim.bat.` |

### 4. Requirements

Nexus requirements (same game):

- MSCLoader (`https://www.nexusmods.com/mysummercar/mods/147`), note: `My Summer Car's mod loader`

Off-site requirements (Skyrim mods live under another game on Nexus, so link them as off-site):

- The Elder Scrolls V: Skyrim Special Edition or Anniversary Edition, `https://store.steampowered.com/app/489830/`, note: `Latest Steam version, or Special Edition 1.5.97`
- SKSE64, `https://skse.silverlock.org`, note: `The build for your Skyrim version`
- Address Library for SKSE Plugins, `https://www.nexusmods.com/skyrimspecialedition/mods/32444`, note: `"Special Edition" file on 1.5.97, "All in one (Anniversary Edition)" otherwise`

### 5. Permissions

- Open source: source at `https://github.com/rackmarten/PerkeleRim` (MIT).
- Allow others to upload, modify, convert and use assets: **Yes**, under the MIT license, with credit.
- Credits: `Inspired by SkyCraft (github.com/chasmlol/SkyCraft). Built on SKSE64, CommonLibSSE-NG, Address Library and MSCLoader.`
- Donation Points: your call.

### 6. Tags

Suggested: Gameplay, Vehicles, Humor, Overhaul, Crossover. Pick whichever of these the tag list actually has.

## Each new release

1. Run `scripts/package.ps1`, then `scripts/nexus-package.ps1`.
2. On Nexus: upload the new zip as a Main file, move the old one to Old versions, bump the mod version.
3. Paste the release's CHANGELOG section into the Changelogs tab.
4. Update the version line ("v0.1.1.") and the known issues in `description.bbcode` if they changed, and paste it again.
