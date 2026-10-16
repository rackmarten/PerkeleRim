using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using PerkeleRim.Game;
using UnityEngine;

namespace PerkeleRim.Spike
{
    // Hidden-MSC spike driver, kept as a debug tool. Lives on a DontDestroyOnLoad object and:
    //  - forces runInBackground and logs tick rate, focus, minimized state and player pose every second
    //  - polls <MSC>/PerkeleRim_cmd.txt (written by scripts/spike.ps1), runs it, then deletes it
    //
    // Commands (first line):
    //   dump [keyword ...]   FSM index + detail files (FsmDump)
    //   continue             main menu only: load the saved game as the Continue button would
    //   beer <count>         a beer case with that many bottles next to the player (BeerGift)
    //   pocket <key>         press a pocket key while handed over: Y, or 3..8 (PocketLink)
    //   hold <kind>          a new pocket kind (0 beer .. 5 macaron box) into MSC's hand, pocket untouched
    //   inspect <text>       objects named like text, inactive ones too: tag, layer, physics, FSMs
    //   items                log the items Skyrim gets (ItemLink): id, path, model id
    //   hands                cameras, layers and first-person renderers to PerkeleRim_hands.txt
    //   handlayer on|off|tint|save  share MSC's first-person layer with Skyrim or not (HandLayerSender); tint toggles a
    //                        red background, save writes the layer to PerkeleRim_handlayer.png;
    //                        handlayer light <ambient> <sun> sets its neutral lighting (0.5 0.8)
    //   snapshot save|load <name>  what Skyrim's MscSave / MscLoad do (SaveSync), with the snapshot in
    //                        <MSC>/PerkeleRim_snapshots/<name> and key 1
    //   island show|hide     draw the collision island, colored by slope, roads bluish green (PlayerLink)
    //   island steps [m]     log the island triangles over 30 degrees within m (8) of the player whose top is within 1 m of the feet
    //   island why [m]       what the ramp pass made of the steep triangles within m (2) of where MSC's camera looks
    //   day on|off          hold MSC at midday (12-14) for testing; the clock jumps back every 2 h
    //   global <name> <v>    set a global PlayMaker float or int (global PlayerUrine 100, global PlayerCigarettes 20)
    //   spawn <product> [n]  a shop product in front of the player, from its Spawner/CreateItems FSM
    //                        (spawn Sausages 2; Pizza, Chips, MacaronBox, Cigarettes, Beer, ...)
    //   event <NAME>         broadcast a global PlayMaker event (DRINKBEER: the beer drinking animation)
    //   textures <name>      read an object's model (GameObject.Find) without sending it and log it
    //   override on|off      InputShim on/off with nothing pressed (on: the link no longer feeds it)
    //   script               following lines are steps: "<seconds>; <token>; <token>..."
    //                        token = KeyCode name (W, LeftShift, Mouse0), axis "Mouse X=3",
    //                        or virtual button "button:Fire1"
    //
    // If <MSC>/PerkeleRim_autoload.txt exists, "continue" runs by itself on the main menu.
    public class SpikeHost : MonoBehaviour
    {
        // Sends SPAWNITEM to one Spawner/CreateItems FSM (it is a global event: broadcast, every shop
        // product would spawn) and moves what it made from the store's bag spot to in front of the player.
        // The spawner copies the condition of whatever lies at the bag spot (an old bag: spoiled food),
        // so the new item's Use FSM is held at Condition 100 while it loads.
        void Spawn(string product, int count)
        {
            PlayMakerFSM spawner = null;
            var items = GameObject.Find("Spawner/CreateItems");
            if (items != null)
                foreach (var f in items.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == product) spawner = f;
            var player = GameObject.Find("PLAYER");
            if (spawner == null || player == null) { SpikeLog.Write($"cmd: no Spawner/CreateItems :: {product} or no PLAYER"); return; }
            var t = player.transform;
            for (int i = 0; i < count; i++)
            {
                spawner.SendEvent("SPAWNITEM");
                var made = spawner.FsmVariables.FindFsmGameObject("New").Value;
                if (made == null) { SpikeLog.Write($"cmd: {product} spawned nothing"); return; }
                made.transform.position = t.position + t.forward * 0.8f + t.right * (0.25f * (i - (count - 1) / 2f)) + Vector3.up * 0.3f;
                var body = made.GetComponent<Rigidbody>();
                if (body != null) body.velocity = Vector3.zero;
                foreach (var f in made.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Use" && f.FsmVariables.FindFsmFloat("Condition") != null) StartCoroutine(KeepFresh(f));
                SpikeLog.Write($"cmd: spawned {made.name} in front of the player");
            }
        }

        // Tag, layer, physics and FSMs of every object whose name contains text (prefabs and inactive
        // templates included), at most 20.
        static void Inspect(string text)
        {
            int n = 0;
            foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
            {
                if (go.name.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++n > 20) { SpikeLog.Write("cmd: inspect: more than 20, stopped"); return; }
                string path = go.name;
                for (var p = go.transform.parent; p != null; p = p.parent) path = p.name + "/" + path;
                var body = go.GetComponent<Rigidbody>();
                var fsms = new List<string>();
                foreach (var f in go.GetComponents<PlayMakerFSM>()) fsms.Add($"{f.FsmName} [{f.ActiveStateName}]");
                SpikeLog.Write($"cmd: inspect {path} active {go.activeInHierarchy} tag {go.tag} layer {go.layer} hide {go.hideFlags} " +
                               $"body {(body != null ? $"{body.mass} kg kinematic {body.isKinematic}" : "none")} colliders {go.GetComponentsInChildren<Collider>(true).Length} " +
                               $"children {go.transform.childCount} at {go.transform.position} fsms {string.Join(", ", fsms.ToArray())}");
            }
            if (n == 0) SpikeLog.Write("cmd: inspect: nothing named like " + text);
        }

        static System.Collections.IEnumerator KeepFresh(PlayMakerFSM use)
        {
            for (float until = Time.realtimeSinceStartup + 3f; Time.realtimeSinceStartup < until && use != null; )
            {
                use.FsmVariables.FindFsmFloat("Condition").Value = 100f;
                yield return null;
            }
            if (use != null) SpikeLog.Write($"cmd: {use.name} condition {use.FsmVariables.FindFsmFloat("Condition").Value}, state {use.ActiveStateName}");
        }

        class Step
        {
            public float Seconds;
            public List<KeyCode> Keys = new List<KeyCode>();
            public List<string> Buttons = new List<string>();
            public Dictionary<string, float> Axes = new Dictionary<string, float>();
            public string Source;
        }

        static SpikeHost instance;
        string cmdPath;
        float nextSecond;
        int updates, fixedUpdates;
        List<Step> script;
        int stepIndex;
        float stepEnds;
        bool autoloadDone;

        static readonly string[] Needs = { "PlayerThirst", "PlayerHunger", "PlayerUrine", "PlayerFatigue", "PlayerDrunk", "PlayerStress" };

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("PerkeleRim.Spike");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SpikeHost>();
        }

        void Awake()
        {
            cmdPath = Path.Combine(SpikeLog.GameDir, "PerkeleRim_cmd.txt");
            Application.runInBackground = true;
            SpikeLog.Write("spike host started");
        }

        void FixedUpdate() => fixedUpdates++;

        void Update()
        {
            updates++;
            RunScript();

            float now = Time.realtimeSinceStartup;
            if (now < nextSecond) return;
            nextSecond = now + 1f;
            Application.runInBackground = true;
            LogProbe();
            updates = fixedUpdates = 0;
            PollCommand();
            HoldDay();

            if (!autoloadDone && Application.loadedLevelName == "MainMenu" && Time.timeSinceLevelLoad > 2f
                && File.Exists(Path.Combine(SpikeLog.GameDir, "PerkeleRim_autoload.txt")))
            {
                autoloadDone = true;
                SpikeLog.Write("autoload: continuing saved game");
                Continue();
            }
        }

        // Puts the Continue button's FSM into the state its click leads to; it then resets the
        // player globals and loads the GAME scene itself. SwitchState is private in PlayMaker 1.6.
        static void Continue()
        {
            var button = GameObject.Find("Interface/Buttons/ButtonContinue");
            PlayMakerFSM fsm = null;
            if (button != null)
                foreach (var f in button.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "SetSize") fsm = f;
            var state = fsm?.Fsm.GetState("Reset globals 2");
            if (state == null) { SpikeLog.Write("continue: button FSM not found (no save?)"); return; }
            typeof(HutongGames.PlayMaker.Fsm)
                .GetMethod("SwitchState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(fsm.Fsm, new object[] { state });
        }

        void LogProbe()
        {
            IntPtr hwnd = FindWindow("UnityWndClass", "My Summer Car");
            bool focused = hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;
            bool minimized = hwnd != IntPtr.Zero && IsIconic(hwnd);
            string pose = "no PLAYER";
            var player = GameObject.Find("PLAYER");
            if (player != null)
            {
                var p = player.transform.position;
                pose = $"pos=({p.x:0.00},{p.y:0.00},{p.z:0.00}) yaw={player.transform.eulerAngles.y:0}";
                foreach (var f in player.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Crouch") pose += $" crouch='{f.ActiveStateName}'";
                foreach (var n in Needs)
                {
                    var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat(n);
                    if (v != null) pose += $" {n.Substring(6).ToLower()}={v.Value:0.#}";
                }
            }
            SpikeLog.Write($"probe fps={updates} fixed={fixedUpdates} focused={focused} minimized={minimized} " +
                           $"rib={Application.runInBackground} ts={Time.timeScale:0.##} scene={Application.loadedLevelName} " +
                           $"override={InputShim.Active} {pose}");
        }

        bool dayLock;

        // Spike "day on": MAP/SUN/Pivot/SUN's Color FSM steps through two-hour states, each setting
        // the sun's rotation, the light and GlobalHour as it starts, and its Clock FSM follows it on
        // WAKEUP. Once a second, put Color back into 12-14 when it has moved on. Only from an hour
        // state, so a save still loads its own time first.
        void HoldDay()
        {
            if (!dayLock || Application.loadedLevelName != "GAME") return;
            var sun = GameObject.Find("MAP/SUN/Pivot/SUN");
            if (sun == null) return;
            PlayMakerFSM color = null, clock = null;
            foreach (var f in sun.GetComponents<PlayMakerFSM>())
            {
                if (f.FsmName == "Color") color = f;
                if (f.FsmName == "Clock") clock = f;
            }
            if (color == null) return;
            var state = color.ActiveStateName;
            if (state == "12-14" || state == null || state.Length != 5 || state[2] != '-') return;
            PerkeleRim.Game.SaveSync.SwitchState.Invoke(color.Fsm, new object[] { color.Fsm.GetState("12-14") });
            if (clock != null) clock.SendEvent("WAKEUP");
            SpikeLog.Write($"day lock: sun {state} -> 12-14");
        }

        void PollCommand()
        {
            if (!File.Exists(cmdPath)) return;
            string[] lines;
            try { lines = File.ReadAllLines(cmdPath); File.Delete(cmdPath); }
            catch (Exception e) { SpikeLog.Write("cmd: read failed " + e.Message); return; }
            if (lines.Length == 0) return;

            var words = lines[0].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return;
            SpikeLog.Write("cmd: " + lines[0].Trim());
            try
            {
                switch (words[0].ToLower())
                {
                    case "dump":
                        var kw = new string[words.Length - 1];
                        Array.Copy(words, 1, kw, 0, kw.Length);
                        FsmDump.Run(kw);
                        break;
                    case "override":
                        InputShim.Clear();
                        InputShim.Active = InputShim.Manual = words.Length > 1 && words[1].ToLower() == "on";
                        break;
                    case "script":
                        StartScript(lines);
                        break;
                    case "continue":
                        Continue();
                        break;
                    case "beer":
                        var link = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (link != null) link.GiveBeer(words.Length > 1 ? int.Parse(words[1]) : 1);
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "items":
                        var lister = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (lister != null) lister.DumpItems();
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "hands":
                        HandsDump.Run();
                        break;
                    case "handlayer":
                        string mode = words.Length > 1 ? words[1].ToLower() : "on";
                        if (mode == "tint") PerkeleRim.Game.HandLayerSender.Tint = !PerkeleRim.Game.HandLayerSender.Tint;
                        else if (mode == "save") PerkeleRim.Game.HandLayerSender.SaveRequested = true;
                        else if (mode == "light" && words.Length > 3)
                        {
                            PerkeleRim.Game.HandLayerSender.Ambient = float.Parse(words[2], CultureInfo.InvariantCulture);
                            PerkeleRim.Game.HandLayerSender.Sun = float.Parse(words[3], CultureInfo.InvariantCulture);
                        }
                        else PerkeleRim.Game.HandLayerSender.Enabled = mode != "off";
                        SpikeLog.Write($"cmd: hand layer {(PerkeleRim.Game.HandLayerSender.Enabled ? "on" : "off")}, tint {PerkeleRim.Game.HandLayerSender.Tint}, light {PerkeleRim.Game.HandLayerSender.Ambient} {PerkeleRim.Game.HandLayerSender.Sun}");
                        break;
                    case "textures":
                        var reader = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (reader != null) reader.InspectModel(lines[0].Trim().Substring(8).Trim());
                        else SpikeLog.Write("cmd: no PlayerLink");
                        break;
                    case "shot":  // MSC's own view, next to the game
                        var shot = Path.Combine(Path.GetDirectoryName(Application.dataPath), "PerkeleRim_shot.png");
                        Application.CaptureScreenshot(shot);
                        SpikeLog.Write("cmd: screenshot to " + shot);
                        break;
                    case "event":  // a global PlayMaker event, e.g. "event DRINKBEER" plays the beer drinking animation
                        if (words.Length > 1) PlayMakerFSM.BroadcastEvent(words[1]);
                        break;
                    case "global":  // set a global PlayMaker float or int, e.g. "global PlayerUrine 100"
                        var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
                        var global = words.Length > 2 ? globals.FindFsmFloat(words[1]) : null;
                        var globalInt = words.Length > 2 && global == null ? globals.FindFsmInt(words[1]) : null;
                        if (global != null) global.Value = float.Parse(words[2], CultureInfo.InvariantCulture);
                        if (globalInt != null) globalInt.Value = int.Parse(words[2], CultureInfo.InvariantCulture);
                        SpikeLog.Write(global != null ? $"cmd: {words[1]} = {global.Value}"
                            : globalInt != null ? $"cmd: {words[1]} = {globalInt.Value}" : "cmd: no such global float or int");
                        break;
                    case "spawn":
                        if (words.Length < 2) { SpikeLog.Write("cmd: spawn <product> [count]"); break; }
                        Spawn(words[1], words.Length > 2 ? int.Parse(words[2]) : 1);
                        break;
                    case "snapshot":
                        if (words.Length < 3 || PerkeleRim.Game.SaveSync.Instance == null) { SpikeLog.Write("cmd: snapshot save|load <name>, in a real build"); break; }
                        var folder = Path.Combine(Path.Combine(SpikeLog.GameDir, "PerkeleRim_snapshots"), words[2]);
                        if (words[1].ToLower() == "save") PerkeleRim.Game.SaveSync.Instance.Save(1, folder);
                        else PerkeleRim.Game.SaveSync.Instance.Load(1, folder);
                        break;
                    case "pick":  // what PlayMaker's MousePickEvent sees: Camera.main's ray through the mouse
                        var cam = Camera.main;
                        SpikeLog.Write($"cmd: Camera.main {(cam != null ? cam.name + " enabled " + cam.enabled + " depth " + cam.depth + " at " + cam.transform.position : "none")}, mouse {Input.mousePosition}, screen {Screen.width}x{Screen.height}");
                        foreach (Camera c in Camera.allCameras)
                            SpikeLog.Write($"cmd: camera {c.name} tag {c.tag} depth {c.depth} mask {c.cullingMask:X8}");
                        if (cam != null)
                            foreach (var hit in Physics.RaycastAll(cam.ScreenPointToRay(Input.mousePosition), 3f))
                                SpikeLog.Write($"cmd: hit {hit.collider.name} ({(hit.collider.transform.parent != null ? hit.collider.transform.parent.name : "")}) layer {hit.collider.gameObject.layer} at {hit.distance:0.00} m trigger {hit.collider.isTrigger}");
                        break;
                    case "death":  // what a fatal crash sends; caught by DeathGuard while handed over
                        PlayMakerFSM.BroadcastEvent("DEATH");
                        break;
                    case "sink":  // "sink 3" pushes the player 3 m down, through the ground (PlayerLink catches the fall)
                        var sunk = GameObject.Find("PLAYER");
                        if (sunk != null) sunk.transform.position += Vector3.down * (words.Length > 1 ? float.Parse(words[1], CultureInfo.InvariantCulture) : 3f);
                        SpikeLog.Write(sunk != null ? $"cmd: player now at {sunk.transform.position}" : "cmd: no PLAYER");
                        break;
                    case "pocket":  // "pocket Y", "pocket 1"
                        var pocketLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (pocketLink == null || words.Length < 2) { SpikeLog.Write("cmd: pocket <Y|3..8>, with PlayerLink"); break; }
                        pocketLink.PressPocketKey(char.IsDigit(words[1][0]) ? (int)KeyCode.Alpha0 + (words[1][0] - (char)48) : (int)KeyCode.Y);
                        break;
                    case "hold":  // "hold 0": a one-bottle beer case in the hand
                        var holder = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (holder != null && words.Length > 1) holder.HoldPocketKind(int.Parse(words[1]));
                        break;
                    case "inspect":  // "inspect empty bottle" lists objects with that name, inactive ones too
                        Inspect(lines[0].Trim().Substring(7).Trim());
                        break;
                    case "island":  // "island show", "island hide", "island steps 8"
                        var islandLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        string islandMode = words.Length > 1 ? words[1].ToLower() : "show";
                        if (islandLink == null) SpikeLog.Write("cmd: no PlayerLink");
                        else if (islandMode == "why") islandLink.LogWhy(words.Length > 2 ? float.Parse(words[2], CultureInfo.InvariantCulture) : 2f);
                        else if (islandMode == "steps")islandLink.LogSteps(words.Length > 2 ? float.Parse(words[2], CultureInfo.InvariantCulture) : 8f);
                        else islandLink.SetIslandVisible(islandMode != "hide");
                        break;
                    case "day":  // "day on", "day off"
                        dayLock = words.Length < 2 || words[1].ToLower() != "off";
                        SpikeLog.Write($"cmd: day lock {(dayLock ? "on" : "off")}");
                        break;
                    case "grunt":  // what Skyrim's PlayerDeath plays
                        PerkeleRim.Game.DeathGuard.PlayDeathSound();
                        break;
                    case "shout":  // "shout 1": what Skyrim's PlayerShout plays for Unrelenting Force, "shout 0" for any other
                        var shoutLink = FindObjectOfType<PerkeleRim.Game.PlayerLink>();
                        if (shoutLink == null) SpikeLog.Write("cmd: no PlayerLink");
                        else shoutLink.Yell(words.Length > 1 ? uint.Parse(words[1]) : 1u);
                        break;
                    default:
                        SpikeLog.Write("cmd: unknown command");
                        break;
                }
            }
            catch (Exception e) { SpikeLog.Write("cmd: FAILED " + e); }
        }

        void StartScript(string[] lines)
        {
            var steps = new List<Step>();
            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(';');
                if (parts.Length == 0 || parts[0].Trim().Length == 0 || parts[0].Trim().StartsWith("#")) continue;
                var step = new Step { Seconds = float.Parse(parts[0].Trim(), CultureInfo.InvariantCulture), Source = lines[i].Trim() };
                for (int j = 1; j < parts.Length; j++)
                {
                    string tok = parts[j].Trim();
                    if (tok.Length == 0) continue;
                    int eq = tok.IndexOf('=');
                    if (eq > 0) step.Axes[tok.Substring(0, eq).Trim()] = float.Parse(tok.Substring(eq + 1).Trim(), CultureInfo.InvariantCulture);
                    else if (tok.StartsWith("button:")) step.Buttons.Add(tok.Substring(7));
                    else step.Keys.Add((KeyCode)Enum.Parse(typeof(KeyCode), tok, true));
                }
                steps.Add(step);
            }
            script = steps;
            stepIndex = -1;
            stepEnds = 0;
            InputShim.Active = InputShim.Manual = true;
            SpikeLog.Write($"script: {steps.Count} steps");
        }

        void RunScript()
        {
            if (script == null) return;
            float now = Time.realtimeSinceStartup;
            if (now < stepEnds) return;

            stepIndex++;
            InputShim.Clear();
            if (stepIndex >= script.Count)
            {
                script = null;
                InputShim.Active = InputShim.Manual = false;
                SpikeLog.Write("script: done");
                LogProbe();
                return;
            }
            var step = script[stepIndex];
            foreach (var k in step.Keys) InputShim.Keys.Add(k);
            foreach (var b in step.Buttons) InputShim.Buttons.Add(b);
            foreach (var a in step.Axes) InputShim.Axes[a.Key] = a.Value;
            stepEnds = now + step.Seconds;
            SpikeLog.Write($"script: step {stepIndex + 1}/{script.Count} '{step.Source}'");
            LogProbe();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsIconic(IntPtr hwnd);
    }
}
