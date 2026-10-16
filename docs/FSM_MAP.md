# My Summer Car: PlayMaker FSM map

Source: `PerkeleRim_fsm_GAME_index.txt` (6617 FSMs) and `PerkeleRim_fsm_GAME_detail.txt` (keywords: PLAYER, Drink, Piss, Urin, Crouch, Death, Drunk, Hunger, Thirst, Fatigue, Sleep, Mount, Seat, Drive). Game version global: `Version = 164`.

Conventions:
- Only things seen in the dumps are stated as fact. Guesses are marked `(unverified)`.
- "Global" means `FsmVariables.GlobalVariables`. "Local" means a variable on that FSM.
- Many FSMs on inactive objects dumped as "(actions failed: ...)": their states and transitions are known, their actions are not. Marked "no actions".
- Paths are `GameObject.Find` style. Objects that are inactive (for example `Systems/Death`) will not be found by `Find`; walk from the parent instead.
- Button names below are cInput action names (see the bindings table at the end). Reading uses `GetButtonDown` / `CInputGetButtonStates`.

## 1. Walking, running, crouch

| Thing | GameObject path | FSM | Notes |
|---|---|---|---|
| Crouch | `PLAYER` | `Crouch` | States: Wait key, Move down 1, Crouched 1, Move down 2, Crouched 2, Move up, Incar, Get out, Seated, Get out 2, In water, Exercise |
| Run / walk speed | `PLAYER` | `Running` | States: No run, Run |
| Apply speed to motor | `PLAYER` | `WalkSpeed` | One state, writes `{PlayerMovementSpeed}` into CharacterMotor max forward/sideways/backward speed every frame |
| Freeze player | `PLAYER` | `Stopping` | Move <-> Stop on global `PlayerStop` (property actions not readable) |
| Water | `PLAYER` | `Swim` | States: Walk, In water, Over water, Out water, Jump, Surfacing, Test drunk, Randomize, Reset, State 1 |
| Cursor / menu / car state | `PLAYER` | `Update Cursor` | Events CARENTER, CAREXIT, MENU, SLEEP, WAKE |
| Reach left (lean) | `PLAYER` | `Reach` | `ReachLeft` button, tilts Pivot 0 to 40 degrees |
| Hand actions | `PLAYER/Pivot/AnimPivot/Camera/FPSCamera` | `PlayerFunctions` | Finger, fist, push, hitchhike thumb, smoking, watch |

### Crouch (`PLAYER :: Crouch`, key `Crouch` = C)
- Verified in game (2026-10-04, scripted C presses while MSC was unfocused): `Wait key` -> `Crouched 1` -> `Crouched 2` -> `Move up` -> `Wait key`. Three positions: standing, crouch, prone.
- `Wait key` (standing). `GetButtonDown(Crouch)` -> FINISHED -> `Move down 1` -> `Crouched 1` (camera y 0.85).
- Press `Crouch` again in `Crouched 1` -> `Move down 2` -> `Crouched 2` (camera y 0.3). Press again -> `Move up` -> `Wait key`.
- `Jump` in `Crouched 1` -> `Move up`. `Jump` in `Crouched 2` -> `Exercise` (PlayerWeight -0.002, PlayerFatigue +0.05, once) -> `Move up`.
- Local vars: `CamPosStanding=1.4`, `CamPosCrouched1=0.85`, `CamPosCrouched2=0.3`, `CrouchSpeed=1`, `PositionCar=0.6`, bools `PlayerInCar`, `PlayerInWater`.
- While in `Crouched 1/2`, `SetFloatValue(PlayerMovementSpeed = CrouchSpeed)` runs every frame.
- Read: `ActiveStateName` of this FSM (`Wait key` = standing, `Crouched 1`, `Crouched 2`). Camera local y of `FPSCamera` is the continuous value.
- Trigger: `SendEvent("FINISHED")` while in `Wait key` would skip the key check (unverified; transition is `FINISHED -> Move down 1`).
- `PlayerInCar` (local, on this FSM) is set by vehicle and proximity triggers: `SetFsmBool(PLAYER, Crouch, PlayerInCar)`. `PlayerInWater` is set by `Swim`. Both force the `Incar` / `In water` states. `PlayerSeated` (global) or `PlayerComputer` forces `Seated`.
- Sits/chairs: `.../CrouchTrigger :: PlayerTrigger` objects (cottage chair, pub chair `STORE/LOD/GFX_Pub/chair_pub 2/CrouchTrigger`, `CABIN/Cabin/Ventti/Chair/CrouchTrigger`, `MAP/BusStop/CrouchTrigger`) set global `PlayerSeated` true on trigger enter and false on exit.

### Run / walk speed (`PLAYER :: Running`, key `Run` = LeftShift)
- `No run`, re-evaluated every 1 s: `MoveSpeed = clamp((280 - PlayerWeight) / 100, 0.9, 2)`, then `PlayerMovementSpeed = MoveSpeed`.
- `GetButtonDown(Run)` -> `Run`: `PlayerMovementSpeed = MoveSpeed * 1.75` (an earlier `MoveSpeed + 1.5` write is overwritten, same state).
- `Run` per second costs: `PlayerThirst += PlayerWeight/400` (local `ThristRate`), `PlayerFatigue += 0.02`, `PlayerDirtiness += 0.16`.
- `Run` exits to `No run` on button release, or if `PlayerMovementSpeed < 1.3`.
- Dump values at capture: `PlayerMovementSpeed = 2`, `PlayerWeight = 78.98`. Run speed at that weight is about 3.5 (derived).
- Read: global `PlayerMovementSpeed` (Float). `Running` active state `Run` = sprinting (unless crouch overrides the speed).
- Related: `PlayerFunctions.PlayerSpeed` (local Float, km/h) is written by vehicle triggers while driving; `Update Cursor :: Loop` resets it to 0.
- `PLAYER :: SpeedStress`: `Rate = clamp((PlayerSpeed - 120)/100, 0, 11)`; while enabled, `PlayerStress` and `PlayerFatigue` each decrease by `Rate` per second. Enabled by `Crouch :: Incar`.

### Swim (`PLAYER :: Swim`)
- Walk -> Over water (raycast hits `LAKEBED`) -> Jump when world y < -5.85 (`Swimming` object activated, `Crouch.PlayerInWater = true`).
- In water: `PlayerDirtiness -4/s`, `PlayerHunger +0.2` and `PlayerFatigue +0.3` (per frame, not per second, as dumped), `Wait 30 s -> DIE`. Leaving the water (y > -5.5) resets.
- `Test drunk`: if `PlayerDrunk > 2.5`, `Randomize` gives 5% DIE per pass. DIE sets `Activate Dead Body.Drown = true` and activates `Death`.

## 2. Drinking

| Item | Value |
|---|---|
| GameObject | `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Drink` |
| FSM | `Drink` (idle state `State 4`) |
| Global flag | `PlayerDrinkOn` (Bool) true during any drink animation; `PlayerHandLeft` also true |
| Global events | `DRINKBEER` -> `Activate`, `DRINKVIINA` -> `Activate 1` (global transitions, work from any state) |
| Other events (idle `State 4` only) | `DRINKSHOT`, `DRINKJUICE`, `DRINKKILJU`, `DRINKMILK`, `DRINKCOFFEE`, `DRINKJAILMILK`, `DRINKCOFFEEGRAN`, `DRINKSPIRIT`, `DRINKCOFFEEHOME`, `MOSQUITO` |

States and per-second effects (applied `perSecond` while the state runs, so total depends on animation length, which is not in the dump):

| Event | State | Alcohol (`PlayerDrunkAdjusted`/s) | Urine/s | Thirst/s | Hunger/s | Other |
|---|---|---|---|---|---|---|
| DRINKBEER | Activate | +0.02 | +1.3 | -5.3 | -0.7 | Weight +0.0184, then stress -3, bottle thrown, Burb |
| DRINKVIINA (booze bottle) | Activate 1 | +0.45 | +2.2 | -5.3 | -0.7 | Weight +0.128, stress -180, event PARTY_TIME |
| DRINKSHOT | Activate 2 | +0.45 | +1.5 | -4 | -0.5 | Weight +0.0128, stress -40 |
| DRINKSPIRIT | Activate 8 | +1.1 | +2.2 | -5.3 | -0.9 | Weight +0.64, stress -180, 20% BLIND -> `METHANOL` event |
| DRINKKILJU | Kilju -> Calc | `+KiljuAlc` (local, x2) | +7 | -12 | -2.5 | Weight +0.07, event YEAST_FEST |
| DRINKMILK | Activate 3 | none | +1.5 | -4 | -4 | Weight +0.09 |
| DRINKJAILMILK | Activate 6 | none | +1.2 | -60 | -0.2 | Weight +0.018 |
| DRINKJUICE | Juice | none | +2 | +1 | -3 | Weight +0.0686 |
| DRINKCOFFEE | Activate 5 | none | +2 | -1 | -1 | Fatigue -6/s, FatigueRate +0.03/s, stress -1.5 |
| DRINKCOFFEEGRAN | Activate 7 | none | +0.65 | -0.3 | -0.3 | Fatigue -2/s, FatigueRate +0.01/s, stress -1.5 |
| DRINKCOFFEEHOME | HomeCoffee | none | scaled by locals `CoffeeHomeCoffee/CoffeeHomeCaffeine` | | | Formula in states |
| MOSQUITO (spray) | Activate 4 / State 1 | none | | | | Adds to `Mosquito/Move.PlayerMosquitoShield` |

(Thirst "-5.3/s" means thirst value goes down, that is the player gets less thirsty. Urine goes up.)

Drunk level computation, `PLAYER/.../FPSCamera/FPSCamera :: Drunk Mode`:
- `PlayerDrunk = PlayerDrunkAdjusted / (PlayerWeight / 83)`, clamped 0..8. `PlayerDrunkAdjusted` also clamped 0..8.
- Each `Update drunk` pass subtracts `Metabolism = 0.0053` from `PlayerDrunkAdjusted` (pass spacing is two 2 s eases, about 4 s, from `UpdateTime = 2`; unverified timing).
- Camera wobble amplitude per axis: X = 8 * PlayerDrunk, Y = 16 * PlayerDrunk, Z = 13 * PlayerDrunk (degrees, eased random target).
- Records `Statistics/Data.MaximumAlcohol`. Global event `WAKEUP` restarts it.
- Hangover (`.../FPSCamera/FPSCamera :: Hangover`): when `PlayerSleeps` true, strength `PlayerDrunk/200`, time `PlayerDrunk*1000`, camera shake, adds small hunger and stress. Saves `HangoverStrenght` and `HangoverLeft`.
- Other drunk uses: `Swim` (above), `SpeakDatabase :: Speech` compares `PlayerDrunk` with 1.5.

Triggers:
- The drink events are sent by item FSMs (`beer case(itemx) :: Use`, `booze :: Use`, `spirit(Clone) :: Use`, `.../KiljuBuyer/CanTrigger :: PlayerDrink`, granny `DrinkCoffee :: Use`). Those item FSMs dumped with "no actions", so the key that triggers them is not visible. `Use` (F) is the likely key (unverified; seen on door and pickup FSMs elsewhere).
- Mod trigger: `Drink` FSM `.SendEvent("DRINKBEER")` (global transition). For the others, send the event while the FSM is in `State 4`. This skips the item and does not consume a bottle (unverified in practice, derived from transitions).
- Read: `PlayerDrinkOn`, `PlayerDrunk`, `PlayerDrunkAdjusted`, `Drink` active state.
- Finished drink: the drinking animation state hands over to the throw: `Drink anim` → `Throw bottle` → `Burb` (beer), `Drink anim 1` → `Throw bottle 1` → `Check day` (booze), `Drink anim 2` → `Throw glass` (shot), `Drink anim 3` → `Throw milk`, `Drink anim 5` → `Throw glass 2` (coffee), `Drink anim 8` → `Throw bottle 2` (spirit), `Kilju` → `Calc`, `Juice` → `Burb`; all end in `State 4`. A new `DRINKBEER`/`DRINKVIINA` (global transitions) cuts a drink short. PerkeleRim heals on these transitions (`ConsumeWatch`).

## 3. Pissing

| Item | Value |
|---|---|
| Main FSM | `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Piss` :: `Logic` (global transition `WAKEUP -> Start urinate`) |
| Helper | same object, `DrinkPiss` (enabled during the stream) |
| Stream object | child `Fluid` of the Piss object (GameObject variable `Fluid`), sound child `PeeSound` |
| Fluid collider FSM | `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/Piss/Fluid/FluidTrigger :: Pouring` (no actions, object inactive) |
| World reaction | `YARD/PissAreas :: Logic` (index only), receives `PISS` events and `PissRate` additions |
| Input | cInput `Urinate` = P |
| Globals | `PlayerUrine` (Float), `PlayerDirtiness`, `PlayerCurrentVehicle` (must be empty), `PlayerSeated`/`PlayerComputer` (block) |

State flow (`Logic`):
1. `Start urinate`: Fluid off. `PlayerSeated` or `PlayerComputer` true -> `Seated` (waits until both false). `GetButtonDown(Urinate)` -> `Check car`.
2. `Check car`: `PlayerCurrentVehicle == ""` -> `State 2`, else back to `Start urinate`.
3. `State 2`: reads `Rain/RoofCheck.RoofCheck` (local `Inside`). Not inside -> `Full power`. Inside -> `State 5`: distance to closest `PissAreas` child < 20 -> `State 3` (sends `PISS`) -> `Full power`; otherwise `Full power`. (So roofed peeing is allowed; the area check only adds the `PISS` event.)
4. `Full power`: enables `DrinkPiss`, activates `Fluid` and `PeeSound`, `Emission = 400`. Every frame: `FluidRate = PlayerUrine * 10`, `EmptyRate = FluidRate / 90`, `PlayerUrine -= EmptyRate` per second, and the same amount added to `PissAreas/Logic.PissRate`. A `SetProperty` (target hidden in the dump) uses it, likely emission rate on the particle system (unverified). No button check here: when `PlayerUrine < 15` -> `Pump`.
5. `Pump`: `PlayerUrine -= 1/s`. `Urinate` pressed again -> `State 4`. `PlayerUrine <= 0` -> `State 1`.
6. `State 4`: `Emission -= 1` per frame, `EmptyRate = Emission/130`, urine drains by `EmptyRate`. Button released, or `Emission <= 10` -> back to `Pump`. `PlayerUrine <= 0` -> `State 1`.
7. `State 1` (end): sends `STOP`, hides `Fluid`, `Radiator1/2` GUI objects, `PlayerDirtiness += 5`, back to `Start urinate`.

Other facts:
- `DrinkPiss`: if `FPSCamera` local x rotation > 276 (looking up) -> `State 2`: `PlayerThirst -= 3/s` and event `ULTIMATE_SURVIVOR`.
- `Pouring` states: Radiator (coolant cap triggers `CapTrigger_Coolant1/2`), Check heat/Steam urine (`StoveTrigger`), `FACE` (State 1), `TV` -> `DEATH` -> `State 14` (`ElectricShockPoint`). Peeing on the sauna stove or TV is a death path (actions not readable).
- Arc tracing: the `Fluid` object is the emitter (path above). Its particle component type is not in the dump (unverified). For an arc, read `Fluid` transform each frame while `Logic` is in `Full power`/`State 4`/`Pump`, or use `Emission` (local float, 400 at start).
- Mod trigger: `Logic.SendEvent("FINISHED")` in `Start urinate` jumps to `Check car` without the key (unverified, derived from transitions). Detect stream: `Logic.ActiveStateName` in {`Full power`, `Pump`, `State 4`} or `Fluid` active.

## 4. Vehicle enter and exit

Entering is by the `DrivingMode` cInput action (Return), not `Use`. The player walks into a trigger zone, a prompt shows (global `GUIinteraction` = "ENTER DRIVING MODE" or "ENTER PASSENGER MODE", `GUIdrive`/`GUIpassenger` true), then `GetButtonDown(DrivingMode)` -> `Key DOWN`.

Per-vehicle driving trigger (FSM `PlayerTrigger` on the `DriveTrigger` object):

| Vehicle | GameObject path (FSM `PlayerTrigger`) | `PlayerCurrentVehicle` | Mode |
|---|---|---|---|
| Satsuma | `SATSUMA(557kg, 248)/PlayerTrigger/DriveTrigger` | `Satsuma` | driving |
| Van (Hayosiko) | `HAYOSIKO(1500kg, 250)/LOD/PlayerTrigger/DriveTrigger` | `Hayosiko` | driving |
| Truck (Gifu) | `GIFU(750/450psi)/LOD/PlayerTrigger/DriveTrigger` | `Gifu` | driving |
| Tractor (Kekmet) | `KEKMET(350-400psi)/LOD/PlayerTrigger/DriveTrigger` | `Kekmet` | driving |
| Moped (Jonnez) | `JONNEZ ES(Clone)/LOD/PlayerTrigger/DriveTrigger` | `Jonnez` | driving |
| Ferndale | `FERNDALE(1630kg)/LOD/PlayerTrigger/DriveTrigger` | `Ferndale` | driving |
| Ruscko | `RCO_RUSCKO12(270)/LOD/PlayerTrigger/DriveTrigger` | `Ruscko` | driving |
| Boat | `BOAT/GFX/Triggers/DriveTrigger` | `Boat` | driving |
| Combine | `COMBINE(350-400psi)/LOD/PlayerTrigger/DriveTrigger` | index only, string not seen | driving (unverified) |
| Fittan (rally) | `TRAFFIC/VehiclesDirtRoad/Rally/FITTAN/PlayerTrigger/DriveTrigger` | `Fittan` | passenger |
| Bus | `NPC_CARS/BusSpawnLoppe/BUS/LOD/PlayerTrigger/DriveTrigger` | `Bus` | passenger |
| Taxi (Kuski) | `NPC_CARS/KUSKI/PlayerTrigger/DriveTrigger` | `EDM` | passenger |
| Kylajani | `NPC_CARS/Amikset/KYLAJANI/LOD/PlayerFunctions/PlayerTrigger/DriveTrigger` | index only | passenger (unverified) |

Common state flow (Satsuma shown, others similar; the index lists the same state names: Wait for player, Press return, Player in car, Create player, Check speed, Passout, Reset view, plus Seatbelt/Death/Speed in some):
- `Wait for player`: sends `DRIVEOFF`, `Entering = false`. Player enters trigger -> `Press return` (prompt, `Entering = true`).
- `Key DOWN` -> `Check seat` (Satsuma: needs seat part installed via `db_Required1 = Seat_Driver` or `db_Required2 = Bucket Seat Driver`, else back to `Wait for player`) -> `Regular seat` / `Racing seat` -> `Reset view` (sends `DRIVE`) -> `Player in car`.
- `Player in car` (seated): `PlayerStop = true`, `PlayerCurrentVehicle` = name above, `VehicleSpawn` = car object, `PlayerFunctions.PlayerSpeed` = speed in km/h every frame, enables FSMs `BeeDeath`, `HeadForce`, `GearIndicator`, `Clutch`, `ButtonShifter` (if `OptionsShifterButton`), pedal `Angle` FSMs, ForceFeedback if `OptionsFFB`. Activates `GUIGearObject`.
- Exit: `DrivingMode` again -> `Seatbelt` (Satsuma: if belt buckled, `Use.IsBuckled`/`Locked` -> stay seated) -> `Check speed` (all velocity axes must be <= `MinimumRequiredVelocity = 0.15`, else stay) -> `In car` -> `Create player`: `PlayerCurrentVehicle = ""`, `PlayerStop = false`, player unparented, back to `Wait for player`.
- `PASSOUT` (global event) -> `Create player 2` (Satsuma) / `Passout` (others): forces exit, clears `PlayerCurrentVehicle`, `PlayerStop = false`. Fittan also has global `DRUNKSPAWN -> Reset`.
- Satsuma `SATSUMA(557kg, 248)/PlayerTrigger :: PlayerTrigger` (separate FSM, states Wait for player, Press return, Player reset) sets `Crouch.PlayerInCar` true while the player is within ~3 m of the car and false otherwise. Gifu, Fittan have equivalents. So `Crouch.PlayerInCar` is a proximity flag as well as a car flag (as dumped).
- Fittan passenger extras while seated: `Hitchhike` (O) lift, `Swear` (N), `Finger` (M), `Drunk` (K) set Fittan `Throttle.SpeedMin/SpeedMax` (driver speed requests).

Read:
- In a vehicle: `PlayerStop == true` AND `PlayerCurrentVehicle != ""`. `PlayerCurrentVehicle` is the best single discriminator.
- `GUIdrive` / `GUIpassenger` = prompt shown, not yet inside.
- Speed: global `SpeedKMH` (Float) and `CarVelocity` (Vector3) exist; the writers were not in the dump (see gaps).
- `PlayerCarControl`, `PlayerVanControl`, `PlayerMuscleControl` (Bool) exist as globals. No FSM in the detail dump sets or reads them. Do not rely on them until re-dumped.
- Mod trigger: `DriveTrigger/PlayerTrigger.SendEvent("Key DOWN")` while in `Press return` enters (player must be inside the trigger); while `Player in car` it starts exit. Unverified in practice.

## 5. Player death

| Item | Value |
|---|---|
| GameObject | `Systems/Death` (inactive until a death; use `GameObject.Find("Systems").transform.Find("Death")`) |
| FSM | `Activate Dead Body` (start state `Permadeath 2`; no actions readable because inactive) |
| Player-side forwarder | `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera :: Death`: global event `DEATH` -> `State 1` sends `GLOBALEVENT`, sets `Activate Dead Body.Crash = true`, activates `Death` |
| Menu/stats | `Systems/Death/GameOverScreen/Orbituary/...` (Data FSMs, no actions), `Systems/Death/PlayerBlood :: Expand/Rot` |
| Globals | `PlayerPermaDeath` (Bool, saved), `OptionsPermaDeath` (Bool, option), `DeathPhysicsForce` (300), `VehicleDeadBody` (GameObject) |

How the game triggers death: set a Bool on `Activate Dead Body`, then `ActivateGameObject(Death)`. Bool variables on the FSM: Burn, Crash, DriveBee, Drown, DrunkAxe, Electrocute, Fatigue, Gasolinefire, Hunger, InJail, Lightning, Murder, PhoneThunder, PissTV, Rally, RunOver, RunOverDrag, RunOverRally, Sewage, Stabbing, Stress, Thirst, Train, Urine, Wasp.

Seen writers:

| Cause | Who sets it | Condition |
|---|---|---|
| Thirst | `Database/PlayerDatabase :: Simulation` `State 6` | `PlayerThirst >= 200` at a 45 s tick (value reset to 0 first) |
| Hunger | `Simulation` `State 7` | `PlayerHunger >= 200` |
| Urine | `Simulation` `State 9` | `PlayerUrine >= 200` |
| Stress | `Simulation` `State 10` | `PlayerStress >= 200` and stays >= 200 for 1 s (`State 1` waits, aborts if it drops) |
| Burn | `Simulation`, event `BURN` | sender not seen |
| Drown | `PLAYER :: Swim` `State 1` | 30 s in water, or 5% per pass when `PlayerDrunk > 2.5` |
| Wasp | `.../FPSCamera/FPSCamera :: Blindness` `State 11` | `MaxAllergy > 100` after a sting |
| Train | `TRAIN/SpawnEast/TRAIN :: Player` | hit by train |
| Sewage | `JOBS/HouseShit1..5/WasteWell_2000litre/Shit/PlayerShitTrigger :: Death` | falls in septic well |
| Crash | `FPSCamera :: Death` via global `DEATH` | sent by vehicle/other FSMs; Jonnez `DriverHeadPivot :: Death` also sends `DEATH` when the head joint is gone. Vehicle `Death` states in the DriveTrigger FSMs exist (no `Death` writer seen) |
| PissTV | `Piss/Fluid/FluidTrigger :: Pouring` -> `State 14` | peeing on the TV shock point (no actions) |

- All stat deaths in `Simulation` are skipped when `OptionsPermaDeath` is false (the `Test Godmode` state routes to a clamp-only loop instead). Name suggests "godmode" but the bool is `OptionsPermaDeath` (as dumped).
- `Activate Dead Body` state graph (no actions): `Permadeath 2` (events DELETE/SAVE) -> `State 3` which fans out by event (BLOOD, RUNOVER, PHONE, SEWAGE, TRAIN, MURDER, LIGHTNING, FATIGUE, HUNGER, URINE, THIRST, STRESS, GASOLINEFIRE, FIRE, AXE, WASP, ELECTROCUTE, DRIVEBEE, RUNOVERRALLY, DRAG, TV, DROWN, INJAIL, STABBING). The Bool-to-event mapping is not readable (unverified). `State 1` -> `State 14` picks a vehicle death by event SATSUMA/HAYOSIKO/KEKMET/FERNDALE/GIFU/FITTAN/JONNEZ/RUSCKO/EDM (probably from `PlayerCurrentVehicle`, unverified). Then Car deathbody/Bloody death/Die 2/Die 3 -> `Sickness 2` -> `Take photo` -> `Save all` -> `Load menu`; with permadeath: `Delete saves` -> `Newspaper` -> `Orbituary`.
- `Activate Dead Body` `State 3` (dumped 2026-10-16) closes the options menus and destroys `PLAYER`'s `FPSInputController`, `CharacterMotor` and `CharacterController` before it fans out by cause, all in the first frame `Systems/Death` is active. Switching it off afterwards is too late. The need death states in `Simulation` (`State 6` thirst, `State 7` hunger, `State 9` urine, `State 10` stress, `Burn`) set the need to 0, set the cause, activate `Systems/Death` and have no transition out.
- Read: `Systems/Death` active; `Activate Dead Body` active state; `PlayerPermaDeath`.
- Mod trigger: replicate the game: `FsmBool Crash`(or another) `.Value = true` then `SetActive(true)`. Or send global event `DEATH` (`PlayMakerFSM.BroadcastEvent("DEATH")`). Untested.

## 6. Needs and stats

Owner of the passive simulation: `Database/PlayerDatabase :: Simulation` (also `NameFix`). It is the only passive updater found.
- Tick: `SimulationRate = 45` s real time (local Float). Each tick adds: `PlayerThirst +1.83`, `PlayerHunger +1.42`, `PlayerUrine +0.24`, `PlayerDirtiness +0.27`, `PlayerStress += PlayerStressRate`, `PlayerFatigue += PlayerFatigueRate`.
- Rates decay each tick: `PlayerStressRate -0.004` (clamp 1.2..`MaxStressRate` 8.5), `PlayerFatigueRate -0.008` (clamp 0.98..`MaxFatigueRate` 8).
- Weight: `PlayerWeight -= PlayerWeight/820000` per second (local `PlayerEnergyRate`), clamp 55..350. New-game defaults: weight 79, stress rate 1.1, fatigue rate 0.98.
- Each tick then checks (when `OptionsPermaDeath` true): thirst, hunger, fatigue, urine, stress at 100 / 150 / 200 as below. `Safe clamp` limits all needs to 0..180 (and `PlayerBurns` to 0), cigarettes 0..20. `Loop` (permadeath off) clamps to about -50..201.
- Saves to `defaultES2File.txt`: PlayerDirtiness, PlayerDrunk (from `PlayerDrunkAdjusted`), Fatigue, Hunger, Money, NetIncome, Thirst, Urine, Fines, Stress, StressRate, FatigueRate, Weight, Cigarettes, PermaDeath, Tattoo.

| Need | Global | Rises | Falls | Thresholds |
|---|---|---|---|---|
| Thirst | `PlayerThirst` | tick +1.83; running +weight/400 per s; punching +0.35; sleep +fatigue/5; sauna | drinks (table in section 2); drinking own urine -3/s | HUD red above 100. 200 = death (`Thirst`) |
| Hunger | `PlayerHunger` | tick +1.42; swim +0.2; hangover +0.007; sleep +fatigue/5 | drinks (mostly small); food (`Use` FSMs, amounts not readable) | HUD red above 100. 200 = death (`Hunger`) |
| Urine | `PlayerUrine` | tick +0.24; drinks (+1 to +7 per s) | peeing | HUD red above 100. 200 = death (`Urine`). Pee needs `PlayerCurrentVehicle` empty |
| Fatigue | `PlayerFatigue` | tick +`PlayerFatigueRate` (0.98 to 8); running +0.02/s; swim +0.3; squat +0.05 | coffee (-6/s, -2/s granny); sleep sets 0; `SpeedStress` while driving fast | HUD red above 100. Above 150 activates `FatigueEyes` (`.../FPSCamera/FPSCamera/FatigueEyes`, eyes closing). `Death.Fatigue` exists but no writer seen. Can sleep only if fatigue >= 10 |
| Stress | `PlayerStress` | tick +`PlayerStressRate` (1.2 to 8.5); hangover +0.0014 | drinks (beer -3, shot -40, bottle -180); speech -0.5; sauna; driving over 120 km/h (`SpeedStress`) | Above 100: sends `SWEARING` events. At 200 for 1 s = death (`Stress`) |
| Dirtiness | `PlayerDirtiness` | tick +0.27; running +0.16/s; peeing +5 on end; sleep +10 | swimming -4/s; shower (`YARD/Building/BATHROOM/Shower/Shower :: Distance`, index only) | `PLAYER/Flies :: Dirtiness` compares at 30/60/90/120/150/180 with CLEAN transitions (fly counts, unverified). No death |
| Drunk | `PlayerDrunk` (derived), `PlayerDrunkAdjusted` (source) | drinks | -0.0053 per `Drunk Mode` pass; sleep subtracts fatigue/5 | 1.5 speech change, 2.5 drowning risk, max 8 |
| Weight | `PlayerWeight` | drinks | ongoing burn; squats | affects walk speed, drunk level, `HungerRate` |

Sleep (`.../LOD_bedroom1/Sleep/SleepTrigger :: Activate`, same FSM on `COTTAGE/LOD/Sleep/...`, `GIFU.../LOD/Sleep/...`, `RCO_RUSCKO12.../LOD/Sleep/...`, `CARPARTS/PartsCar/seat rear(Clone)/Sleep/...`, `ITEMS/sofa(itemx)/Sleep/...`):
- Prompt "SLEEP" (global `GUIinteraction`) within ray 2; `Use` key or left mouse -> `ACTIVATE`. Blocked if `PlayerSeated` or `PlayerCurrentVehicle` not empty (except in vehicles with a sleep trigger). Requires `PlayerFatigue >= 10` (`Check sleepy`).
- Sleep length by fatigue: < 20 = 2 h, < 40 = 4 h, < 60 = 6 h, < 80 = 8 h, otherwise 10 h (events 1 to 5 map to 2/4/6/8/10).
- `Calc rates`: `Rate = PlayerFatigue/5`; `PlayerDirtiness +10`, `PlayerDrunkAdjusted -Rate`, `PlayerHunger/Thirst/Urine +Rate`, `PlayerFatigue = 0`.
- Sets global `PlayerSleeps` true while asleep, `PlayerStop` true, sends `SLEEP`, later `WAKE`. Advances `GlobalDay` (wraps after 7 to 1).
- `Systems/PlayerWanted :: Activate` (arrest/jail) applies a similar reset: `Rate = Fatigue/4`, fatigue 0, drunk 0, dirtiness 5.

Smoking: `PlayerFunctions` `Smoking` key `I`, needs `PlayerCigarettes > 0` ("NO CIGARETTES" otherwise), blocked if `PlayerHelmet` or `PlayerHandRight`. Flow: `Initiate` (`GetButtonDown(Smoking)`) → `Check hand 2` → `Check tobacco` (`IntCompare PlayerCigarettes` with 0) → `Activate` (activates the `Smoking` object, local `Smoking = true`) or `No cigaretes`. Effects are in `.../FPSCamera/FPSCamera/Smoking :: Start/Hand` (actions not readable while inactive): `Reset` → `Anim 1` → `Wait player`, `SMOKE` puffs (`Double` → `Anim 2` → `Inhale` → `Anim 3` → `Outhale` → `Stop smoke`), `STOP` puts it out (`Put out` → `Dump cigarette 2`); the `Explosion?` states check for a gasoline nozzle nearby. `CANT_STOP_SMOKING_NOW` achievement fires when `PlayerStressRate` is near its 8.5 max.

Middle finger (dumped 2026-10-15): `PlayerFunctions` `Finger` key `M`. `Initiate` (`GetButtonDown(Finger)` → `MIDDLEFINGER`) → `Check hand 3` (back to `Initiate` if `PlayerHandLeft`) → `Finger` (shows `MiddleFinger`, `PlayerHandLeft = true`, a random `Fuck` sound and its line in `GUIsubtitle`, `middlefinger` animation) → `Fuck singer` (hand free again; near the singer, sometimes `FUCK` → `State 2`) → `Initiate`. `Finger 2`/`Thumb Up`/`Finger 3` are the hitchhike thumb (O), not the finger. PerkeleRim shouts Fus on `Finger` (`FusLink`).

Watch (dumped 2026-10-15): `PlayerFunctions` `Watch` key `U`. `Initiate` (`GetButtonDown(Watch)` → `WATCH`) → `Check hand 6` (back to `Initiate` if `PlayerHandLeft`) → `Anim on` (shows `Watch`, `hand_watch_on`) → `Watch ON` (`BUTTON` → `Watch ON 2` if the key is still held, else after 0.5 s `BACK`) → `Watch ON 2` (held up while the key is pressed, `PlayerHandLeft = true` every frame) → `Anim off` (`PlayerHandLeft = false`, `hand_watch_off`) → `Close` (hides `Watch`) → `Initiate`. Only the key's press in `Initiate` starts it. PerkeleRim's guard (right click, `BlockButton`) holds U for the arm.

Eating: each food item's `Use` FSM (`pizza`, `macaron box`, `sausages`, `potato chips`, `pike`, `moosemeat`) goes `Wait button` / `Wait button 2` → `USE` → `Eat` / `Eat 2` → `FINISHED` → `Destroy` (`STOP` → back to waiting). A single sausage (`sausage0 :: Use`): `USE` → `State 2` → `1` → `Grilled` or `2` → `Fresh` → `Destroy`. Bottles (`booze`, `milk`, `spirit`) pass `Eat 2` too, then `Play anim` → `Check drink`, which sends the `Drink` FSM's event. The item FSMs' actions are not readable ("actions failed"), so the hunger each food takes off is unknown.

## Global variables of interest

| Name | Type | Meaning (seen in dump) |
|---|---|---|
| `PlayerMovementSpeed` | Float | Current walk/run/crouch speed, fed to the motor |
| `PlayerWeight` | Float | kg; speed and drunk scaling, 55..350 |
| `PlayerDrunk` / `PlayerDrunkAdjusted` | Float | Drunk level 0..8 (derived / source) |
| `PlayerThirst` `PlayerHunger` `PlayerUrine` `PlayerFatigue` `PlayerStress` `PlayerDirtiness` | Float | Needs, HUD scale 0..100, death/effects at 100/150/200 |
| `PlayerStressRate` `PlayerFatigueRate` | Float | Per-45 s tick increments |
| `PlayerBurns` `PlayerAllergy` | Float | Burn and wasp allergy level (usage minimal in dump) |
| `PlayerMoney` `PlayerFines` `PlayerFinesDay` `PlayerNetIncome` | Float | Money and fines |
| `PlayerDrinkOn` | Bool | A drink animation is running |
| `PlayerHandLeft` `PlayerHandRight` | Bool | Hand busy flags (drink, push, finger, watch) |
| `PlayerSeated` | Bool | On a chair (CrouchTrigger zones) |
| `PlayerComputer` | Bool | Using the in-game computer |
| `PlayerSleeps` | Bool | Asleep |
| `PlayerStop` | Bool | Player movement frozen (in vehicle, sleeping, menus) |
| `PlayerInMenu` | Bool | A menu is open (cursor unlocked) |
| `PlayerCurrentVehicle` | String | `""` on foot, else Satsuma, Hayosiko, Gifu, Kekmet, Jonnez, Ferndale, Ruscko, Boat, Fittan, Bus, EDM |
| `PlayerWanted` | Bool | Police chase flag |
| `PlayerHelmet` | Bool | Helmet on |
| `PlayerSeatbeltsOn` | Bool | Seatbelt flag (not written in dumped FSMs) |
| `PlayerCarControl` `PlayerVanControl` `PlayerMuscleControl` | Bool | Exist, no readers/writers in dump |
| `PlayerPermaDeath` / `OptionsPermaDeath` | Bool | Permadeath state / option; gates stat deaths |
| `PlayerCigarettes` | Int | 0..20 |
| `PlayerKeySatsuma` `PlayerKeyHome` `PlayerKeyGifu` `PlayerKeyHayosiko` `PlayerKeyFerndale` `PlayerKeyRuscko` | Int | Key ownership |
| `SpeedKMH` `RPM` `CarVelocity` `PlayerVelocity` `PlayerCrashEnergy` | Float/Vector3 | Vehicle telemetry (writers not dumped) |
| `GUIinteraction` `GUIuse` `GUIdrive` `GUIpassenger` | String/Bool | On-screen prompt text and flags |
| `GlobalDay` `GlobalHour` `ClockHours` `ClockMinutes` | Int/Float | Time of day, day of week (1..7) |
| `ItemInHand` `SavePlayer` `SavePlayerCam` `POV` `TheCar` | GameObject | Handy refs (Satsuma, player, camera) |
| `PlayerDir` | Vector3 | Look direction (set by `Update Cursor`) |

The globals list also contains a service URL and credential-like strings (`WWW*`); they are deliberately not reproduced here.

## Carrying items

The hand is `PLAYER/Pivot/AnimPivot/Camera/FPSCamera/1Hand_Assemble/Hand :: PickUp` (dumped live). Idle `Look for object`: `MousePick` 1 m on layers 19/15/9 into `RaycastHitObject` every frame, left click -> `State 1` (`PickedObject = RaycastHitObject`) -> `Check moped?` -> `Check if Item` (tag `ITEM`) / `Check if Part` (tag `PART`: store items, beer cases, food, empty bottles) -> `Set pivot` (hand and `ItemPivot` moved to the object) -> `Part picked`: the object is parented to `ItemPivot`, layer 16, held by the hand's joint. There left click or Alpha2 drops (`Drop part`), right click throws, `Use` on an `ITEM` equips it. Globals `DROP_PART`/`DROP_TOOL` drop from any state; after a drop `Wait` (0.1 s) leads back to `Look for object`.

Item `Use` FSMs mouse-pick on layers 19 and 16, so a carried item is used with F like one on the ground (a held beer case is drunk from). `beer case(itemx) :: Use`: `Remove bottle` destroys a random child and counts `DestroyedBottles`; empty -> `State 4` renames it `empty(itemx)`. Items have a global `GARBAGE` (case: `Is garbage`, `Consumed = true`; food: `Destroy`, `Condition = 888`): renderer and collider removed, moved to `GarbageSpawnPos`, destroyed on the next load. Spoiling food renames itself `spoiled <name>`. Store items' names: `beer case(itemx)`, `sausages(itemx)`, `pizza(itemx)`, `potato chips(itemx)`, `macaron box(itemx)`, spawned by `Spawner/CreateItems` FSMs `Beer`, `Sausages`, `Pizza`, `Chips`, `MacaronBox` (`SPAWNITEM`, result in `New`). The bottle thrown after a beer is a copy of the inactive root prefab `BottleBeerFly` (0.5 kg, `PART`), renamed `empty bottle(Clone)` by its `Go` FSM, which also tosses it; it isn't saved. A cigarette pack (`cigarettes0 :: Use`) goes into `PlayerCigarettes` when used (`Check pocket` -> `Add inventory`).

## cInput bindings (primary key, no secondary binds set)

| Action | Key | Action | Key |
|---|---|---|---|
| PlayerLeft / Left | A | Crouch | C |
| PlayerRight / Right | D | Jump / Handbrake | Space |
| PlayerUp / Up | W | Run | LeftShift |
| PlayerDown / Down | S | Zoom | LeftControl |
| Use | F | ReachLeft / ReachRight | Q / E |
| Urinate | P | Hitchhike | O |
| DrivingMode (enter/exit vehicle) | Return | Swear | N |
| Smoking | I | Hit (punch) | H |
| Watch | U | Push | J |
| Finger | M | Drunk (passenger "ease" request) | K |
| ThrottleOn / BrakeOn | W / S | ClutchOn | X |
| ShiftUp / ShiftDown | G / B | Range | R |
| IndicatorLeft / Right | Semicolon / Colon | HighBeam / Wipers | L / K |
| Boost | T | StartEngine, reverse, neutral, gears 1 to 6 | unbound |

Not cInput but read directly: Alpha1 and Alpha2 switch between the hand and the tools (`2Spanner`), and Alpha2 also drops what `PickUp` carries. Note `Drunk` and `Wipers` both default to K. There is no drink action: drinking is triggered by item `Use` FSMs.

## Coverage gaps and re-dump keywords

The mod's `dump <keyword...>` matches FSM path or name.
- Vehicle control/telemetry (who sets `PlayerCarControl`, `SpeedKMH`, `RPM`, starter, ignition, throttle): not covered. Try `dump Throttle Starter Ignition Dashboard CarSimulation`.
- Eating and food: state graphs known (section above); the actions (hunger taken off) are not readable. Try a dump with the item in hand.
- Washing (dirtiness down): `Shower :: Distance` is index only. Try `dump Shower Sauna Wash`.
- Drink item FSMs (`beer case`, `booze`, `spirit`) and `Systems/Death` showed "(actions failed)" because the objects were inactive. Re-dump with a bottle in hand and while dead: `dump beer booze spirit Death`.
- Smoking effects: state graph known, actions not readable while `Smoking` is inactive. Re-dump while smoking: `dump Smoking`.
- Piss emitter and area reactions: `dump Fluid PeeSound PissAreas Rain`. The particle component name needs a hierarchy or component dump, not available here.
- Vehicle death causes (who sends `DEATH`, crash thresholds): `dump Crash Collision DeathBody DriverHeadPivot`.
- Burn and fire: `dump FIRE Burn`. Passenger vehicles Kylajani and Combine: only indexed.
- Hunger/thirst/fatigue HUD files: only `Hunger`, `Urine`, `Fatigue` HUD detail exists (`Thrist`, `Stress`, `Dirty` are index-only; same Scale pattern, red above 100 verified for the three dumped).
