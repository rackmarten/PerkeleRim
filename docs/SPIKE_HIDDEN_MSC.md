# Spike: hidden My Summer Car

> Run 2026-10-04 on the Windows dev box (Skyrim 1.7.104 + SKSE 2.3.1, MSC version global 164, MSCLoader 1.4.2, Unity 5.0.0f4, Harmony 1.2, PlayMaker 1.6).
> Goal: prove MSC can be driven while hidden before building the plumbing on that assumption.

## Verdict

| Item | Result |
|---|---|
| MSC keeps simulating while unfocused or minimized | **Go** |
| MSC takes movement, look and action input from outside its window | **Go** |
| PlayMaker FSMs needed later are mapped | **Go** ([FSM_MAP.md](FSM_MAP.md)) |
| Skyrim player can be placed every frame from an external pose | **Go** |

## 1. Simulating while hidden

- The spike mod sets `Application.runInBackground = true` at startup and re-asserts it every second.
- Measured with a per-second probe (Update and FixedUpdate counts, window focus and `IsIconic`):

| State | Frame rate | FixedUpdate rate |
|---|---|---|
| Focused | 90-145 fps | 50 Hz |
| Unfocused (behind other windows) | 90-145 fps | 50 Hz |
| Minimized | 85-105 fps | 50 Hz |

- Physics runs at a steady 50 Hz in every state, and that's what matters for the simulation. The frame rate is uncapped; capping it later (`Application.targetFrameRate`) will save CPU while Skyrim runs.
- The needs globals keep ticking while hidden (thirst, hunger and so on rose during the runs).

## 2. Input from outside the window

**Approach that works:** redirect `UnityEngine.Input` at the call sites.

- MSC reads input in two ways: cInput (rebindable actions, used by the PlayMaker `CInput*` actions) and plain `UnityEngine.Input` (MouseLook, Standard Assets controllers, PlayMaker `GetKey`/`GetMouseButton` actions). cInput itself reads `UnityEngine.Input` underneath.
- `UnityEngine.Input` methods are engine internal calls, so Harmony can't patch them directly. Instead, at startup the mod scans the game assemblies (`Assembly-CSharp*`, `Assembly-UnityScript*`, `cInput`, `PlayMaker`) for methods that call `Input.*` and transpiles those calls to a shim with the same signatures (`msc/src/Spike/InputShim.cs`, `InputRedirect.cs`).
- Result: 8768 methods scanned, 90 patched, 252 call sites redirected, about 3 s at startup. Left alone: mouse position, touch, accelerometer, joystick names and `ResetInputAxes`, none of which matter here.
- While the shim is active, the game only sees a virtual keyboard, virtual axes and virtual mouse buttons, with per-frame Down/Up edges. While it's inactive, everything passes through, so normal play is unaffected.
- Verified while MSC was unfocused or minimized, by scripted input:
  - walk (W) about 2.0 m/s, run (W+LeftShift) about 3.5 m/s, strafe and backwards
  - mouse look (`Mouse X` axis) turns the player
  - crouch (C) cycles standing, crouch and prone (`PLAYER :: Crouch` states)
- Not exercised yet: pissing (urine was too low in the test save) and vehicles. Both go through the same keys (P, Return), so no new risk is expected.
- Protocol consequence: `InputState` can carry raw key states plus the `Mouse X`/`Mouse Y` axes (and `Horizontal`/`Vertical` for scripts that read Unity axes). The cInput action names to keys table is in FSM_MAP.md.
- Fallback, not needed: send window messages to the hidden MSC window. That's fragile (Unity's focus handling) and was never tried.

## 3. FSM map

- The spike mod can dump every PlayMaker FSM (path, name, states) plus full detail for keyword matches. 6617 FSMs in the GAME scene.
- [FSM_MAP.md](FSM_MAP.md) covers walking/crouch, drinking, pissing, vehicle enter/exit, death and needs, plus known gaps and the keywords for re-dumps.
- The raw dumps stay in the game folder and are never committed: they include game-internal strings such as a service URL and credentials.

## 4. Skyrim player positioning

- Test (`skse/src/spike/PoseTest.cpp`): hook `PlayerCharacter::Update` (vfunc 0xAD), run the native update, then place the player on a scripted 4 m circle. F10 cycles Off, `Place` (`Actor::SetPosition(pos, true)`) and `PlaceStill` (adds heading and zeroes the character controller's linear velocity).
- Measured drift (how far the native update moved the player away from our pose between frames), 60 fps:
  - flat ground: 0.1-0.5 units average (a few mm; about 70 units per metre)
  - slopes: spikes of 2-6 units, because the test keeps Skyrim's own height without terrain sampling. In the real design the height comes from MSC standing on the collision island.
  - one 208-unit spike when the player slid down a slope in the same height-keeping case
- Observed: movement and first-person camera are smooth in both modes. Holding W while placed plays the walk animation but doesn't move the player off the circle, so the native controller doesn't fight the external pose.
- Third person shows no locomotion animation, because the player is placed rather than walked. Not a problem for first person. If needed later, drive the animation graph's speed and direction variables.
- Choice for the plumbing: `PlaceStill` (position, heading, zeroed velocity) every frame in the `PlayerCharacter::Update` hook, Skyrim movement input blocked while MSC owns the player.

## Dev loop (also an outcome of the spike)

- `scripts/launch-msc.ps1`: starts MSC windowed, so the `-screen-*` arguments skip Unity's config dialog, then presses Space past the news screen.
- `<MSC>/PerkeleRim_autoload.txt`: the mod continues the saved game from the main menu (it switches the Continue button's FSM into its click state).
- `scripts/msc-save.ps1 snapshot|restore <name>`: known save states outside the repo.
- `scripts/spike.ps1`: sends `dump`, `continue`, `override on|off` or an input script (`scripts/spike/*.txt`) to the running game and tails the spike log.
- Result: build, deploy, launch and test MSC with no clicks. About 30 s from launch to standing in the game.

## Follow-ups for the plumbing increment

- Replace the scripted input driver with the shared-memory `InputState`.
- Keep the spike's probe and dump tools as a debug option; consider `Application.targetFrameRate` for the hidden instance.
- Re-dump with the keywords in FSM_MAP.md "Coverage gaps" when the drinking, food and vehicle work starts.
- A test save outdoors with urine and beer available would make piss and drink tests one command.
