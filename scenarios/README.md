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
| `model-read.json` | MSC's frames while it reads and sends its models (`scripts/model-read-test.ps1`): every model read again with new ids, as after MSC starts (`pr.send models again cold`), then again under the same ids, as after Skyrim restarts (`models again`), each waited out with `pr.models`; MSC logs each stretch's worst frame (`model: models again cold: ... worst <n> ms`) and each frame over 200 ms with how much of it was reading and sending models; the script fails such a frame with over 100 ms of that and no garbage collection in it (one takes 150 to 300 ms of MSC's frame, whoever's allocation set it off). The Satsuma is brought and half built first, so its parts' models are among those read. Then the van, the moped, the Kekmet and the Gifu called one by one and pictured from outside, and the Satsuma | 66 s |
| `fuel-hint.json` | where to refuel: Kumpo at the van's wheel in Riverwood (`drive HAYOSIKO`, then Return), the tank set full, low and dry; a full tank has no fuel line, a low one names the nearest pump with its distance and direction ("Fuel 6.0 / 40 L. Whiterun Dwemer Oil ..."), a dry one shows the tow tip once; pictures with the HUD of the low and the dry tank | 20 s (Riverwood's load included) |
| `teimo.json` | Teimo at his stall in Riverwood: drawn as MSC's Teimo behind the counter (pictures from the front, close up, the side and above), Kumpo walking up to the counter and Teimo greeting him with a caption, turning to him, small talk while he hangs about, and his answer to the middle finger; which line he said each time (`pr.teimo`'s `lastClip`, `PerkeleRim.log`'s `teimo: says`) | 34 s |
| `satsuma.json` | the Satsuma drawn as MSC has it built (`scripts/satsuma-test.ps1`): brought in front of Kumpo south of the shack as the starter save has it (a bare body), then half built through MSC's own assembly (`pr.satsuma half`: body panels, front seats, dashboard, radiator); each time every part on it drawn from its own model (`allDrawn`), MSC's view (`msc-bare.png`, `msc-half.png`, copied in by the script) and Skyrim's (`bare-kumpo.png`, `half-kumpo.png`) from Kumpo's eyes the same moment, and pictures from the side and the front. Needs a fresh session: the Satsuma stays half built | about 40 s |
| `satsuma-garage.json` | the Satsuma moved over to its bay by the garage once (`scripts/satsuma-garage-test.ps1` runs it with a fresh MSC save and a half built one): the session's handover at the garage does the move (`moved`, the body on its stand, over 40 parts on their own slots, none in the bay off them), pictures of the bay from the front, the shelves, the back and the body; then saved and loaded again: the load only tells MSC the body lives on its stand (`last.request == 16`, one request since the load), and every part is still on its slot | about 70 s |
| `garage-board.json` | the garage board brings things home: the van called to Riverwood and left there, the gearbox dropped in Bleak Falls Barrow (`pr.send fetch gearbox(Clone)`), then back at the garage the board's two answers: the van onto the garage's pit (its position checked), the gearbox back on its slot (none off); pictures of both. After `satsuma-garage.json`, or in a fresh session | about 90 s |
| `teimo-drunk.json` | Kumpo walking up drunk (MSC drunk 2): Teimo greets him, then remarks on the drink with a caption; backing off past 8 m gets his goodbye | 26 s |
| `teimo-shop.json` | Shopping at Teimo's stall: MSC's goods set out on it (pictures from the front and above), a beer case taken (`shop take beer`), put on the counter (`shop counter`) and on the bill at MSC's price (149), Kumpo talks to Teimo (`pr.teimo.talk`) and pays: the gold drops from 300 to 151, the case becomes MSC's beer case item (`msc.log`: `stall: paid for ..., now beercase<n>`), Teimo thanks him with a caption; another case with 20 gold left: not enough money (MSC's NOT ENOUGH MONEY, Teimo's `ei_tarpeeksi_rahaa`); then the case carried off down the road unpaid: a 149 gold bounty in Whiterun Hold, Teimo's shout, the case a real item in the hand | 44 s |
| `teimo-shop-drunk.json` | Buying at the stall sober and drunk: sausages sober get his thanks (`paid`), two beer cases paid at once at MSC's drunk level 2 (298) become two real cases and get one of his lines for a drunk ordering (`drunkbuy`, `tilaus_kanni*`) | 30 s |
| `look-at.json` | MSC's hand on the van's driver door handle, with Kumpo stood there by MSC's `aim` debug command: right after the start (the van on the garage's pit), after a load of the starter save (`pr.load`, as a quickload does), and with the van called on open grass; MSC's prompt as Skyrim got it (`pr.lookat`), a screenshot with the HUD each time | about 40 s |
| `whirlwind-sprint.json` | Whirlwind Sprint while MSC has Kumpo: the van called in front of him stops a dash, one word dashes him about 15 m the way he faces, three about 30 m, Fire Breath doesn't move him (`pr.shout`, so not the Z key itself) | 15 to 17 s |
| `satsuma-bolting.json` | building the Satsuma the way MSC does it: the car brought in front of Kumpo (`pr.satsuma here`) and stripped (`bare`), its body held still as on a stand (`freeze`); the left door, the front left wheel and the radiator each picked up by MSC's own hand (`fetch <part> eyes`, a left click), counted as held into its place (`offer <trigger>`: MSC checks the hand and shows its assemble icon, which has to reach Skyrim, `pr.lookat`), clicked on, bolted with MSC's wrench (2, crouched, `wrench` for the size as if picked from the spanner set, `look ... BoltPM #n` at each bolt, `pr.wheel 10`), unbolted (`pr.wheel -10`) and taken off with a right click on MSC's disassemble icon; MSC's state through the probe at each step (`pr.part`: on the car, bolted, every bolt tight, and Skyrim drawing it on the car), pictures of both games (`screenshot`, and MSC's own view by `pr.mscshot` in Documents/My Games/PerkeleRim/Screenshots). The bare car has no brake under the front left wheel: its setup tells MSC's database the disc brake is on | about 90 s |
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
  one more), 2018 ms sending them again under the same ids (4 over 200 ms); with the stepped reader, over seven runs, 146
  to 186 ms in 10 of the 14 stretches (3 or 4 frames over 100 ms each), the van read in 0.3 to 0.7 s of steps over about
  40 frames instead of 1.7 to 2.5 s in one, and the whole stretch over in 16 s instead of 25. The other four: a 229 ms
  frame with 190 ms of model work in it, a garbage collection in the Gifu's encode (since then a model is no longer
  encoded into one array, the Gifu's was 63 MB); a 1101 ms frame after the reading was over, from before MSC logged each
  long frame's share; and frames of 203 to 374 ms in two runs while other sessions loaded the desktop, with 0 to 16 ms of
  model work in them. The longest steps (120 to 170 ms) are a few that take 3 to 15 ms in other runs: a garbage
  collection stopping in them (MSC logs the count per long frame) or a texture's read back waiting for the GPU. The gaps
  between chunks stay under 1.5 s, 4.7 s on the loaded desktop; the session's readiness and the hit test take 8 s
  without a chunk as MSC done. On a loaded desktop a link-up's few garbage collections land in the reads' steps as 200 to
  350 ms frames: every such frame with model work in it had one. A session start's own link-up also holds a
  9.3 s frame, MSC loading its game (logged as `no model read in it`), with either reader. All four vehicles look as before.
- `van-lights.json` (5 runs): the low beams put out darkened the ground ahead by 42 luma (86 to 44, two thirds of its pixels), the
  ground beside the van rose by 0.9 (eye adaptation); MSC switching every lamp off darkened the ground ahead by 47 to 57.
- `whirlwind-sprint.json` (23 November 2026, 3 runs, one right after `hitbox.json`): 15.0 and 30.0 m on open grass, 3.0 to 3.2 m into the van (MSC logs `stopped by a wall`), 0 for Fire Breath.
- `fuel-hint.json` (23 November 2026, 1 run): 27 of 27 steps; with the van 481 m south of the Whiterun pump spot (bearing
  352 degrees) the line read "Fuel 6.0 / 40 L. Whiterun Dwemer Oil 480 m north", then "Fuel 0.1 / ..." with the tow tip top
  left, above the needs bars; the fuel line on its box above the gear, clear of everything else.
- `look-at.json` (23 November 2026, 15 runs, 9 of them with My Summer Car's window never focused): every one passed;
  MSC's own raycast had the door handle under the crosshair at 0.74 to 0.77 m, its `Use` FSM in `Mouse over 1` and
  `GUIuse` true, and Skyrim got the hand within 0.3 s of the look.

- `teimo.json` (3 runs in one session, 23 November 2026): 27 of 27 steps each. Teimo said a different greeting, story and answer
  to the finger each run (`paivaa5`, `random8`, `keskari4`; `paivaa7`, `random17`, `keskari5`; `paivaa2`, `random4`, `keskari4`), the
  greeting 4.0 m out, as Kumpo reached the counter, and he faced Kumpo within 10 degrees a second later. Kumpo stops at the
  counter, 98.5 units from Teimo, however long he keeps walking. `teimo-drunk.json` (1 run): the greeting, the drunk remark
  2.7 s later (`teimo_huomio_kannin1`), the goodbye (`kiitos2`) as he backed past 8.0 m.
- `teimo-shop.json` (5 runs in two sessions, 24 November 2026): every one passed once the test put the second case down
  beside the paid one rather than on it (an earlier run dropped it on top and it slid off the counter). MSC read all 11
  prices from its shop (beer 149, motor oil 149, sausages 15.09); paying 149 with 300.73 marks left 151.73, Skyrim's gold
  151. His thanks varied (`kiitos5`, `kiitos1`, `kiitos4`, `kiitos2`), the theft came at 8.7 m. `teimo-shop-drunk.json`
  (6 runs; the last 2 with two cases in one payment, `beercase10` and `beercase11` for 298): sober thanks, then
  `tilaus_kanni5` or `tilaus_kanni6` for the beer. 8 runs of the four shop and Teimo scenarios in one session passed
  after the review's fixes.

The rear lenses: `lightson 1` also lights MSC's brake and reverse lamps, which its own state machines switch off again
or not. With them on, the left lens was 18 to 20 luma darker with the lights off (mostly red); with only the tail lamps
on, the lenses hardly change (-0.4 to +2.5): Skyrim's tail lamps show little glow at night. That's why the scenario only
records those numbers.

## Writing one

Start from the closest file. Steps are the kit's requests (`docs/COMMANDS.md` in the kit) and PerkeleRim's own:

| Command | What it does |
|---|---|
| `pr.msc [on=<bool>]` | Kumpo to MSC (the default) or back to Skyrim, as F10 does; answers once it's done (`msc`, `seated`) |
| `pr.send <line>` | a line for MSC's debug commands (`msc/src/Spike/SpikeHost.cs`): `drunk 2`, `lightson 1`, `time 6`, `hold 0`, `drive HAYOSIKO` (into the van's driving trigger; `pr.key 13`, Return, then gets in), `fsmvar FuelTank Data FuelLevel 6` (every tank's level), `aim doorl Handle` (Kumpo stood at that control surface, looking at it; `aim stop` lets go), `look trigger_door_left` (the same at any collider, `#2` the second nearest), `wrench door left(Clone)` (MSC's wrench the size of that part's bolts), `parts door` (the Satsuma's parts into MSC's log), `fetch door left eyes` (a loose part held under the crosshair for a click), `offer trigger_door_left` (the part in hand counted as come into its place), `freeze SATSUMA` (the body held still), `fit` and `tighten` (setups through MSC's own assembly and bolts), ... |
| `pr.key <Unity KeyCode> [seconds=<s>]` | holds one of MSC's keys (93 is ] to call the van, 104 H to punch) |
| `pr.shout [words] [shout=<hex>] [seconds=<s>]` | tells MSC the player shouted, as Combat does when a shout is released (Whirlwind Sprint unless `shout=`, 3 words by default), and answers 2 s later with how far Kumpo went, flat, in metres (`moved`) |
| `pr.load <save>` | loads a save in the running game, as a quickload does (answers Skyrim's question about installed Creations), and answers once it's loaded; play mode hands Kumpo to MSC a little later, `pr.msc` waits for that |
| `pr.lookat` | MSC's look-at prompt as Skyrim last got it: `hand` (MSC's hand on a door handle, switch or knob), `assemble` (a held part in its place on the car), `disassemble` (a fitted part with its bolts out), `icon` (the one Skyrim draws: `hand`, `assemble`, `disassemble` or `none`), `text`, and `msc` (whether MSC has Kumpo) |
| `pr.part [name]` | one of the Satsuma's parts as MSC's database has it (any spelling: `door_left`, `radiator`): `installed`, `bolted`, `damaged`, `placed`, `attached` and `drawnOnCar` (in the Satsuma's `PartSet`, drawn on the car in Skyrim), `bolts`, `boltsTight`, `stages`, `allTight`, `boltsOut`, `tightness`, `tightnessFull`, where; without a name, how many MSC reported and how many are on the car |
| `pr.wheel <notches> [every=<s>]` | turns MSC's scroll wheel (positive away from you: MSC's wrench tightens the bolt it is on), a notch every 0.35 s |
| `pr.mscshot <name>` | MSC's own view as `<name>_msc.png` in the folder F11 saves to, and its path once saved |
| `pr.call [vehicle]` | calls the van (]) or the moped ([) and answers once it stands solid in Skyrim, with where it is |
| `pr.fuel` | the fuel line as Skyrim shows it (`line`, with the nearest pump on a low tank), MSC's `low` and `dry` flags for it, and whether this save had the tow tip (`tipped`) |
| `pr.vehicle [vehicle]` | a vehicle MSC has on the island (van, moped, kekmet, gifu, satsuma): where, how fast, drawn, solid, which of its lights are on (`lightRolesOn`: 1 low, 2 high, 3 tail, 4 brake, 5 reverse, 6 cabin, 7 work, 8 beacon); the Satsuma also how many parts are on it (`attached`), without a model yet (`unread`), drawn (`partsDrawn`) and whether all of them are (`allDrawn`) |
| `pr.satsuma [here\|half\|bare]` | the Satsuma: `here` brings it in front of Kumpo as MSC has it built, `half` puts half its parts on through MSC's own assembly, `bare` takes every part with a Removal FSM off; answers once it is drawn and its parts have settled, as `pr.vehicle satsuma` |
| `pr.garage` | the Satsuma bay: whether the save had the move (`moved`), bay requests sent since the load (`requests`), MSC's last answer (`last`: `request`, `body`, `slotted`, `floor`, `missed`, `vehicles`) and the bay marker (`bay`) |
| `pr.garage.ask <move\|parts\|vehicles\|check\|stand>` | asks MSC for one of the bay's jobs (the move again, the board's parts or vehicles home, a count of the parts on and off their slots, the body back on its stand) and answers with its `GarageDone`, as `pr.garage` |
| `pr.garage.board <vehicles\|parts>` | uses the garage board as Kumpo does: its question asked and the answer clicked; answers with MSC's `GarageDone`, as `pr.garage` |
| `pr.hits [scenarios] [target=<hex>]` | the hit test, a row per attack |
| `pr.hitbox` | the hitbox test, a row per line it logs |
| `pr.models [quiet=<s>]` | waits until MSC's model chunks have come and then stopped for quiet seconds (8), with how many came |
| `pr.teimo` | Teimo at his stall: found, loaded, drawn as MSC's Teimo, where, which way he faces, how far the player is, the last line he said (`lastClip`, `lastTopic`, `lines`) and his caption now; `pr.teimo:` is his camera and probe target (his middle, facing the way his model looks) |
| `pr.teimo.say <topic>` | Teimo says a line of a topic now (`greet`, `drunk`, `chat`, `bye`, `finger`, `pissed`, `hit`), as if its event came |
| `pr.teimo.front [distance=<units>]` | moves the player (Skyrim has it) in front of the stall, facing Teimo |
| `pr.teimo.reset` | Teimo forgets Kumpo's visit and his line count, as after a load |

The cameras and probes also take `pr.part:<name>` (a Satsuma part where MSC has it, its bolt state as the middle, facing out of the open front) and `pr.vehicle:<van|moped|kekmet|gifu>`: the vehicle's origin, facing its way, with its
drawn model and lamps inside (`probe lights pr.vehicle:van`, `light off pr.vehicle:van name=LowBeam`; the lamps are
named `PerkeleRimLowBeam`, `PerkeleRimHighBeam`, `PerkeleRimTailLight`, `PerkeleRimBrakeLight`,
`PerkeleRimReverseLight`, `PerkeleRimCabinLight`, `PerkeleRimWorkLight`, `PerkeleRimBeacon`). After a `light off` at
a target, give them back with a plain `light on`: `light on pr.vehicle:van` only gives back lights within 100 units of
the van's middle.

Read every screenshot a scenario takes before trusting its numbers.
