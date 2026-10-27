using System;
using System.Diagnostics;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's state goes with Skyrim's saves (MscSave / MscLoad), as an in-place savestate:
    //  - MscSave: captures what can be put back without reloading MSC, from memory in a few ms, into
    //    the save's snapshot folder (Snapshot): needs, money and the like (PlayMaker globals), and
    //    while MSC has the player also the vehicles (pose, speed, fuel) and the items near the player
    //    (pose, beer case bottle counts). MSC's own save is left alone (broadcasting SAVEGAME
    //    mid-game makes its FSMs act as if quitting, and stalls it for seconds);
    //  - MscLoad: when the folder holds the key Skyrim's cosave has, the globals go back right away;
    //    vehicles and items wait for the handover Skyrim resumes after loading such a save, and go
    //    back once the player stands on the new island. Without a matching snapshot MSC stays as it is.
    // Only captured state rolls back; MSC still saves its own game when it quits.
    public class SaveSync
    {
        // Vehicles and items wait this long (seconds) for the resumed handover.
        const float WorldTimeout = 30f;
        // A load that comes before MSC is in its game (both just started) waits this long for it,
        // then GameSettle more once it's there.
        const float GameTimeout = 180f;
        const float GameSettle = 3f;

        static readonly string[] Floats =
        {
            "PlayerThirst", "PlayerHunger", "PlayerStress", "PlayerUrine", "PlayerFatigue", "PlayerDirtiness",
            "PlayerDrunk", "PlayerDrunkAdjusted", "PlayerStressRate", "PlayerFatigueRate", "PlayerWeight", "PlayerBurns",
            "PlayerFines", "PlayerNetIncome", "GlobalTime",
        };
        // Not PlayerCigarettes or PlayerMoney: Skyrim's inventory (the pocket, its gold) holds them in
        // the same save, and PocketLink copies them to MSC; restoring the snapshot's would fight it.
        static readonly string[] Ints = { "GlobalDay", "GlobalHour" };

        // FSM states and variables, put back in this order (entering a state resets its variables).
        // Time of day: the sun's 2-hour block (Color, which also turns the sun pivot to the block's
        // start), the clock's hour (Clock), then the sun's angle and the minutes within the hour.
        // The states' own timers start over, so the current hour runs its full length again.
        static readonly string[][] FsmStates =
        {
            new[] { "MAP/SUN/Pivot/SUN", "Color" },
            new[] { "MAP/SUN/Pivot/SUN", "Clock" },
        };
        static readonly string[][] FsmFloats =
        {
            new[] { "MAP/SUN/Pivot/SUN", "Color", "Minutes" },
            new[] { "MAP/SUN/Pivot/SUN", "Clock", "Hours" },
            new[] { "MAP/SUN/Pivot/SUN", "Clock", "Minutes" },
            new[] { "MAP/SUN/Pivot", "Rotation", "Rotation" },
        };
        static readonly string[][] FsmInts =
        {
            new[] { "MAP/SUN/Pivot/SUN", "Color", "Time" },
            new[] { "MAP/SUN/Pivot/SUN", "Clock", "Time" },
        };

        public static SaveSync Instance { get; private set; }

        readonly Island island;
        readonly VehicleLink[] vehicles;
        readonly ItemLink items;
        readonly Func<bool> playing;
        readonly Func<Transform> player;
        readonly Action<string> log;

        Snapshot waiting;      // a whole load waiting for MSC to be in its game
        string waitingFolder;
        uint waitingAfter;     // island generation when the load came
        float waitingUntil;
        float playerSince;     // when MSC's player showed up, -1 = not yet

        Snapshot pending;      // vehicles and items waiting for the handover
        uint pendingAfter;     // island generation when the load came; applied on a newer one
        float pendingUntil;

        public SaveSync(Island island, VehicleLink[] vehicles, ItemLink items, Func<bool> playing, Func<Transform> player, Action<string> log)
        {
            this.island = island;
            this.vehicles = vehicles;
            this.items = items;
            this.playing = playing;
            this.player = player;
            this.log = log;
            Instance = this;
        }

        public void OnMessage(MessageType type, byte[] payload, int size)
        {
            ulong key;
            string folder;
            if (!Snapshot.Decode(payload, size, out key, out folder))
            {
                log($"save: malformed {type} message ({size} bytes)");
                return;
            }
            if (type == MessageType.MscSave) Save(key, folder);
            else Load(key, folder);
        }

        public void Save(ulong key, string folder)
        {
            var clock = Stopwatch.StartNew();
            var who = player();
            if (who == null)
            {
                log($"save: MSC isn't in its game ({Application.loadedLevelName}), no snapshot for {System.IO.Path.GetFileName(folder)}");
                return;
            }
            var s = new Snapshot { Key = key, World = playing() };
            CaptureGlobals(s);
            CaptureFsms(s);
            if (s.World)
            {
                foreach (var v in vehicles)
                {
                    var b = v.Capture(who);
                    if (b != null) s.Vehicles.Add(b);
                }
                s.Items.AddRange(items.Capture(who));
            }
            try
            {
                s.Store(folder);
                log($"save: snapshot to {folder} in {clock.Elapsed.TotalMilliseconds:0.0} ms: {s.Globals.Count + s.Fsms.Count} values{TimeOfDay()}" +
                    (s.World ? $", {s.Vehicles.Count} vehicles, {s.Items.Count} items" : ", MSC didn't have the player") + $" (key {key:X16})");
            }
            catch (Exception e) { log($"save: snapshot to {folder} FAILED: {e.Message}"); }
        }

        public void Load(ulong key, string folder)
        {
            waiting = null;  // a newer load replaces one still waiting
            var s = Snapshot.Load(folder);
            if (s == null || s.Key != key)
            {
                log(s == null
                    ? $"load: no MSC snapshot in {folder}, MSC stays as it is"
                    : $"load: {folder} holds key {s.Key:X16}, not {key:X16}, MSC stays as it is");
                return;
            }
            waitingAfter = island.Generation;
            if (player() == null)
            {
                // Both games just started: Skyrim loaded its save before MSC is back in its game.
                waiting = s;
                waitingFolder = folder;
                waitingUntil = Time.realtimeSinceStartup + GameTimeout;
                playerSince = -1f;
                log($"load: MSC isn't in its game yet ({Application.loadedLevelName}), {folder} waits for it");
                return;
            }
            Apply(s, folder);
        }

        void Apply(Snapshot s, string folder)
        {
            int values = ApplyGlobals(s) + ApplyFsms(s);
            pending = s.World ? s : null;
            pendingAfter = waitingAfter;
            pendingUntil = Time.realtimeSinceStartup + WorldTimeout;
            log($"load: {values} values back from {folder}{TimeOfDay()}" + (s.World ? $", {s.Vehicles.Count} vehicles and {s.Items.Count} items wait for the handover" : ""));
        }

        // A load that came before MSC was in its game: applied once MSC has been in it for
        // GameSettle (MSC's own FSMs load their saved values in the first moments and would
        // overwrite ours).
        void ApplyWaiting()
        {
            if (Time.realtimeSinceStartup > waitingUntil)
            {
                log($"load: MSC didn't get into its game in time, it doesn't go back to {waitingFolder}");
                waiting = null;
                return;
            }
            if (player() == null) { playerSince = -1f; return; }
            if (playerSince < 0f) playerSince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - playerSince < GameSettle) return;
            var s = waiting;
            waiting = null;
            Apply(s, waitingFolder);
        }

        // Every LateUpdate, after the vehicles and items had theirs (so a new anchor's parking and
        // carrying are done and get overridden here).
        public void Update()
        {
            if (waiting != null) ApplyWaiting();
            if (pending == null) return;
            if (Time.realtimeSinceStartup > pendingUntil)
            {
                log("load: no handover came, vehicles and items stay where they are");
                pending = null;
                return;
            }
            if (!playing() || island.Generation == pendingAfter) return;
            var s = pending;
            pending = null;
            var who = player();
            foreach (var b in s.Vehicles)
                foreach (var v in vehicles)
                {
                    if (v.ObjectName != b.Name) continue;
                    if (b.Driven != v.Carries(who))
                        log($"load: the player {(b.Driven ? "sat" : "didn't sit")} in the {v.Name} in the save, and that stays as it is now");
                    v.Restore(b);
                }
            items.Restore(s.Items, who);
        }

        static void CaptureGlobals(Snapshot s)
        {
            var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            foreach (var name in Floats)
            {
                var f = globals.FindFsmFloat(name);
                if (f != null) s.Globals.Add(new Snapshot.Global { Kind = 'f', Name = name, Value = f.Value });
            }
            foreach (var name in Ints)
            {
                var i = globals.FindFsmInt(name);
                if (i != null) s.Globals.Add(new Snapshot.Global { Kind = 'i', Name = name, Value = i.Value });
            }
        }

        // Enters a state the way a transition does (its actions run); private in PlayMaker 1.6.
        internal static readonly System.Reflection.MethodInfo SwitchState = typeof(HutongGames.PlayMaker.Fsm)
            .GetMethod("SwitchState", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        internal static PlayMakerFSM FindFsm(string path, string name)
        {
            var go = GameObject.Find(path);
            if (go == null) return null;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == name) return fsm;
            return null;
        }

        static void CaptureFsms(Snapshot s)
        {
            foreach (var r in FsmStates)
            {
                var fsm = FindFsm(r[0], r[1]);
                if (fsm != null && !string.IsNullOrEmpty(fsm.ActiveStateName))
                    s.Fsms.Add(new Snapshot.FsmValue { Path = r[0], Fsm = r[1], Kind = 's', Name = fsm.ActiveStateName });
            }
            foreach (var r in FsmFloats)
            {
                var fsm = FindFsm(r[0], r[1]);
                var v = fsm != null ? fsm.FsmVariables.FindFsmFloat(r[2]) : null;
                if (v != null) s.Fsms.Add(new Snapshot.FsmValue { Path = r[0], Fsm = r[1], Kind = 'f', Name = r[2], Value = v.Value });
            }
            foreach (var r in FsmInts)
            {
                var fsm = FindFsm(r[0], r[1]);
                var v = fsm != null ? fsm.FsmVariables.FindFsmInt(r[2]) : null;
                if (v != null) s.Fsms.Add(new Snapshot.FsmValue { Path = r[0], Fsm = r[1], Kind = 'i', Name = r[2], Value = v.Value });
            }
        }

        static bool Listed(string[][] table, Snapshot.FsmValue v)
        {
            foreach (var r in table)
                if (r[0] == v.Path && r[1] == v.Fsm && (r.Length < 3 || r[2] == v.Name)) return true;
            return false;
        }

        // States first, then variables; only FSMs and names this build captures.
        internal static int ApplyFsms(Snapshot s)
        {
            int n = 0;
            foreach (var v in s.Fsms)
            {
                if (v.Kind != 's' || !Listed(FsmStates, v)) continue;
                var fsm = FindFsm(v.Path, v.Fsm);
                var state = fsm != null ? fsm.Fsm.GetState(v.Name) : null;
                if (state == null) continue;
                SwitchState.Invoke(fsm.Fsm, new object[] { state });
                n++;
            }
            foreach (var v in s.Fsms)
            {
                var fsm = v.Kind == 's' ? null : FindFsm(v.Path, v.Fsm);
                if (fsm == null) continue;
                if (v.Kind == 'f' && Listed(FsmFloats, v))
                {
                    var f = fsm.FsmVariables.FindFsmFloat(v.Name);
                    if (f != null) { f.Value = (float)v.Value; n++; }
                }
                else if (v.Kind == 'i' && Listed(FsmInts, v))
                {
                    var i = fsm.FsmVariables.FindFsmInt(v.Name);
                    if (i != null) { i.Value = (int)v.Value; n++; }
                }
            }
            return n;
        }

        // ", time 18:01 (state 18)" from MSC's clock, for the log.
        static string TimeOfDay()
        {
            var clock = FindFsm("MAP/SUN/Pivot/SUN", "Clock");
            var hours = clock != null ? clock.FsmVariables.FindFsmFloat("Hours") : null;
            var minutes = clock != null ? clock.FsmVariables.FindFsmFloat("Minutes") : null;
            return hours != null && minutes != null ? $", time {hours.Value:00}:{minutes.Value:00} (state {clock.ActiveStateName})" : "";
        }

        // Only names this build captures; a snapshot can't set anything else.
        static int ApplyGlobals(Snapshot s)
        {
            var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            int n = 0;
            foreach (var g in s.Globals)
            {
                if (g.Kind == 'f' && Array.IndexOf(Floats, g.Name) >= 0)
                {
                    var f = globals.FindFsmFloat(g.Name);
                    if (f == null) continue;
                    f.Value = (float)g.Value;
                    n++;
                }
                else if (g.Kind == 'i' && Array.IndexOf(Ints, g.Name) >= 0)
                {
                    var i = globals.FindFsmInt(g.Name);
                    if (i == null) continue;
                    i.Value = (int)g.Value;
                    n++;
                }
            }
            return n;
        }
    }
}
