# In-game scenarios

PerkeleRim's in-game checks as skse-test-kit scenario files: steps and checks run against a test session with
`scripts/skytest.ps1 run scenarios/<name>.json` (docs/DEV_SETUP.md, "Test sessions"). Each writes
`out/<time>-<name>/`: `results.csv` (a row per check), `responses.jsonl`, `step-<n>-rows.csv` for a step with rows,
its screenshots and `summary.json`. `run` exits 0 when every step passed, 1 when one didn't, 2 when Skyrim crashed,
exited or hung.

Every one starts from the session's default start, the starter save `PerkeleRim-Kumpo` with Kumpo handed to My Summer
Car (he wakes in the shack beside the garage), and runs in any order: the ones that need room first move him onto the
open grass south of the shack (Skyrim takes him back, `player.setpos`, MSC takes him again there). Nothing is saved in
either game.

| File | What it checks | Time from a running session |
|---|---|---|
| `test-kit-smoke.json` | MSC has Kumpo, the player is in the world, MSC's debug commands reach it, PerkeleRim's commands are there | under 1 s |
| `hits.json` | the hit test (`scripts/hit-test.ps1`): punches sober and drunk, a thrown bottle, thrown junk, bottles drunk and drunker at a Whiterun guard with its AI off, each attack's damage against `protocol/cpp/damage.h` and the guard's health; a row per attack in `step-9-rows.csv` | 73 to 92 s |
| `hitbox.json` | the hitbox test (`scripts/hitbox-test.ps1`): the van called in front of Kumpo, 8 arrows shot at him through it (all stop at the van), 4 from his side (they reach him), a melee bandit behind it who walks round it to him, never through; every line of the test in `step-8-rows.csv` | 65 s |
| `model-read.json` | MSC's frames while it reads and sends its models (`scripts/model-read-test.ps1`): every model read again with new ids, as after MSC starts (`pr.send models again cold`), then again under the same ids, as after Skyrim restarts (`models again`), each waited out with `pr.models`; MSC logs each stretch's worst frame (`model: models again cold: ... worst <n> ms`) and the script fails a frame over 200 ms. Then the van, the moped, the Kekmet and the Gifu called one by one and pictured from outside | 66 s |
| `whirlwind-sprint.json` | Whirlwind Sprint while MSC has Kumpo: the van called in front of him stops a dash, one word dashes him about 15 m the way he faces, three about 30 m, Fire Breath doesn't move him (`pr.shout`, so not the Z key itself) | 15 to 17 s |
| `van-lights.json` | the van's lights at night: MSC switches them on and Skyrim's lamps follow (both low beams, the rear lamps, the cabin); pictures from behind, in front and above; an A/B pair of one paused frame with the low beams put out (the ground ahead goes dark, the ground beside doesn't change); MSC switching every lamp off | 9 s |

## What the numbers were

Measured on the test desktop on 13 November 2026 (Skyrim AE 1.7.104, SKSE 2.3.1, My Summer Car with MSCLoader 1.3.1),
in a hidden session:

- `hits.json`: 17 of 17 rows passed (16 attacks and the drunk-against-sober ratio, x1.7 at drunk 2) in 5 of 6 runs. The
  other lost `drunker-bottle 2`: the drunk camera's sway sent three of five throws past the guard and the two re-throws
  were counted per scenario, so the second hit had none left. They're counted per throw now; all runs since passed.
- `hitbox.json` (5 runs, the same each time): 8 of 8 arrows through the van stopped at it (on the hitbox's layer, about 142 units from the van's
  middle, its back end), none came nearer Kumpo than 559 units; 4 of 4 control arrows reached him; the bandit stood for
  about 26 s, then walked round the van's side to within 90 units of Kumpo, never inside its box.
- `model-read.json` (23 November 2026, 11 models: the four vehicles, the blood overlay and six items, 3924 chunks, 193 MB):
  MSC's worst frame reading every model again was 2009 ms with the old reader (5 frames over 200 ms: a vehicle each and
  one more), 2018 ms sending them again under the same ids (4 over 200 ms); with the stepped reader 146 to 172 ms and
  150 to 151 ms over two runs (none over 200 ms, 3 over 100 ms each), the van read in 0.3 to 0.5 s of steps over about 40 frames instead of 1.7 to
  2.5 s in one, and the whole stretch over in 16 s instead of 25. The longest steps (120 to 150 ms) are a few that take 3 to
  15 ms in other runs, most likely Mono's garbage collector stopping in them; the gaps between chunks stay under 1.3 s. A session start's own link-up also holds a
  9.3 s frame, MSC loading its game (logged as `no model read in it`), with either reader. All four vehicles look as before.
- `van-lights.json` (5 runs): the low beams put out darkened the ground ahead by 42 luma (86 to 44, two thirds of its pixels), the
  ground beside the van rose by 0.9 (eye adaptation); MSC switching every lamp off darkened the ground ahead by 47 to 57.
- `whirlwind-sprint.json` (23 November 2026, 3 runs, one right after `hitbox.json`): 15.0 and 30.0 m on open grass, 3.0 to 3.2 m into the van (MSC logs `stopped by a wall`), 0 for Fire Breath.

The rear lenses: `lightson 1` also lights MSC's brake and reverse lamps, which its own state machines switch off again
or not. With them on, the left lens was 18 to 20 luma darker with the lights off (mostly red); with only the tail lamps
on, the lenses hardly change (-0.4 to +2.5): Skyrim's tail lamps show little glow at night. That's why the scenario only
records those numbers.

## Writing one

Start from the closest file. Steps are the kit's requests (`docs/COMMANDS.md` in the kit) and PerkeleRim's own:

| Command | What it does |
|---|---|
| `pr.msc [on=<bool>]` | Kumpo to MSC (the default) or back to Skyrim, as F10 does; answers once it's done (`msc`, `seated`) |
| `pr.send <line>` | a line for MSC's debug commands (`msc/src/Spike/SpikeHost.cs`): `drunk 2`, `lightson 1`, `time 6`, `hold 0`, ... |
| `pr.key <Unity KeyCode> [seconds=<s>]` | holds one of MSC's keys (93 is ] to call the van, 104 H to punch) |
| `pr.shout [words] [shout=<hex>] [seconds=<s>]` | tells MSC the player shouted, as Combat does when a shout is released (Whirlwind Sprint unless `shout=`, 3 words by default), and answers 2 s later with how far Kumpo went, flat, in metres (`moved`) |
| `pr.call [vehicle]` | calls the van (]) or the moped ([) and answers once it stands solid in Skyrim, with where it is |
| `pr.vehicle [vehicle]` | a vehicle MSC has on the island: where, how fast, drawn, solid, which of its lights are on (`lightRolesOn`: 1 low, 2 high, 3 tail, 4 brake, 5 reverse, 6 cabin, 7 work, 8 beacon) |
| `pr.hits [scenarios] [target=<hex>]` | the hit test, a row per attack |
| `pr.hitbox` | the hitbox test, a row per line it logs |
| `pr.models [quiet=<s>]` | waits until MSC's model chunks have come and then stopped for quiet seconds (8), with how many came |

The cameras and probes take `pr.vehicle:<van|moped|kekmet|gifu>`: the vehicle's origin, facing its way, with its
drawn model and lamps inside (`probe lights pr.vehicle:van`, `light off pr.vehicle:van name=LowBeam`; the lamps are
named `PerkeleRimLowBeam`, `PerkeleRimHighBeam`, `PerkeleRimTailLight`, `PerkeleRimBrakeLight`,
`PerkeleRimReverseLight`, `PerkeleRimCabinLight`, `PerkeleRimWorkLight`, `PerkeleRimBeacon`). After a `light off` at
a target, give them back with a plain `light on`: `light on pr.vehicle:van` only gives back lights within 100 units of
the van's middle.

Read every screenshot a scenario takes before trusting its numbers.
