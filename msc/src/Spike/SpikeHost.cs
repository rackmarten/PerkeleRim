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
    //   beer <count>         hand out beer as if Skyrim sent GiveBeer (BeerGift)
    //   items                log the items Skyrim gets (ItemLink): id, path, model id
    //   hands                cameras, layers and first-person renderers to PerkeleRim_hands.txt
    //   handlayer on|off|tint|save  share MSC's first-person layer with Skyrim or not (HandLayerSender); tint toggles a
    //                        red background, save writes the layer to PerkeleRim_handlayer.png
    //   global <name> <v>    set a global PlayMaker float (global PlayerUrine 100)
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
                        else PerkeleRim.Game.HandLayerSender.Enabled = mode != "off";
                        SpikeLog.Write($"cmd: hand layer {(PerkeleRim.Game.HandLayerSender.Enabled ? "on" : "off")}, tint {PerkeleRim.Game.HandLayerSender.Tint}");
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
                    case "global":  // set a global PlayMaker float, e.g. "global PlayerUrine 100"
                        var global = words.Length > 2 ? HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat(words[1]) : null;
                        if (global != null) global.Value = float.Parse(words[2], CultureInfo.InvariantCulture);
                        SpikeLog.Write(global != null ? $"cmd: {words[1]} = {global.Value}" : "cmd: no such global float");
                        break;
                    case "death":  // what a fatal crash sends; caught by DeathGuard while handed over
                        PlayMakerFSM.BroadcastEvent("DEATH");
                        break;
                    case "grunt":  // what Skyrim's PlayerDeath plays
                        PerkeleRim.Game.DeathGuard.PlayDeathSound();
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
