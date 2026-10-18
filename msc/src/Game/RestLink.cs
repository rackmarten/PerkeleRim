using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Skyrim's Wait menu and beds, and its loads, as they reach MSC's player:
    //  - TimePassed: the clock (sun, hour, day) moves on by the hours waited or slept and the needs
    //    change by MSC's own sleep rates (Link/Rest.cs);
    //  - LoadReset: whatever the player was doing is stopped before a loaded save gives control back
    //    (Link/LoadReset.cs), so a piss, a cigarette or a raised hand never carries over. The needs
    //    come from the save (SaveSync).
    //  - PlayerWashed: Kumpo waded into Skyrim's water up to his waist, so his dirtiness goes to 0.
    public class RestLink
    {
        const string SunPath = "MAP/SUN/Pivot/SUN";
        const string PivotPath = "MAP/SUN/Pivot";
        const float MaxHours = 48f;

        readonly System.Action<string> log;

        public RestLink(System.Action<string> log)
        {
            this.log = log;
        }

        // ---- Waiting and sleeping ----

        public unsafe void OnTimePassed(byte[] payload, int size)
        {
            if (payload == null || size < sizeof(TimePassed))
            {
                log($"rest: malformed TimePassed ({size} bytes)");
                return;
            }
            TimePassed t;
            fixed (byte* p = payload) t = *(TimePassed*)p;
            if (!(t.hours > 0f) || t.hours > MaxHours)
            {
                log($"rest: TimePassed of {t.hours} hours ignored");
                return;
            }
            bool bed = (t.flags & Wire.TimeBed) != 0;
            var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            var fatigue = globals.FindFsmFloat("PlayerFatigue");
            var hunger = globals.FindFsmFloat("PlayerHunger");
            var thirst = globals.FindFsmFloat("PlayerThirst");
            var urine = globals.FindFsmFloat("PlayerUrine");
            if (fatigue == null || hunger == null || thirst == null || urine == null)
            {
                log("rest: MSC isn't in its game, TimePassed ignored");
                return;
            }
            var needs = Rest.Hours(fatigue.Value, t.hours, bed);
            double before = fatigue.Value;
            fatigue.Value += (float)needs.Fatigue;
            hunger.Value += (float)needs.Hunger;
            thirst.Value += (float)needs.Thirst;
            urine.Value += (float)needs.Urine;
            log($"rest: {t.hours:0.##} h {(bed ? "in a bed" : "waiting")}: fatigue {before:0.#} -> {fatigue.Value:0.#}, hunger/thirst/urine +{needs.Hunger:0.##}{ClockMove(t.hours)}");
        }

        // Moves the sun, the clock and the day on by the hours; ", time 18:01 -> 02:01, day 3 -> 4" for the log.
        // Also the spike's "time N", for filming MSC at another hour.
        public static string ClockMove(float hours)
        {
            var color = SaveSync.FindFsm(SunPath, "Color");
            var clock = SaveSync.FindFsm(SunPath, "Clock");
            var pivot = SaveSync.FindFsm(PivotPath, "Rotation");
            var day = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
            if (color == null || clock == null || pivot == null || day == null) return ", clock not found";
            var colorTime = color.FsmVariables.FindFsmInt("Time");
            var colorMinutes = color.FsmVariables.FindFsmFloat("Minutes");
            var clockHours = clock.FsmVariables.FindFsmFloat("Hours");
            var clockMinutes = clock.FsmVariables.FindFsmFloat("Minutes");
            if (colorTime == null || colorMinutes == null || clockHours == null || clockMinutes == null) return ", clock variables not found";

            var from = new Rest.Time
            {
                Sun = Rest.SunOf(colorTime.Value, colorMinutes.Value),
                Clock = Rest.ClockOf(clockHours.Value, clockMinutes.Value),
                Day = Mathf.Clamp(day.Value, 1, 7),
            };
            var to = Rest.Advance(from, hours);

            var s = new Snapshot();
            AddState(s, "Color", Rest.ColorState(to.Sun));
            AddState(s, "Clock", Rest.ClockState(to.Clock));
            AddFloat(s, SunPath, "Color", "Minutes", Rest.BlockMinutes(to.Sun));
            AddFloat(s, SunPath, "Clock", "Hours", Rest.ClockHours(to.Clock));
            AddFloat(s, SunPath, "Clock", "Minutes", Rest.ClockMinutes(to.Clock));
            AddFloat(s, PivotPath, "Rotation", "Rotation", Rest.Rotation(to.Sun));
            s.Fsms.Add(new Snapshot.FsmValue { Path = SunPath, Fsm = "Color", Kind = 'i', Name = "Time", Value = Rest.ColorTime(to.Sun) });
            SaveSync.ApplyFsms(s);
            day.Value = to.Day;
            return $", sun {Hhmm(from.Sun)} -> {Hhmm(to.Sun)}, clock {Hhmm(from.Clock)} -> {Hhmm(to.Clock)}, day {from.Day} -> {to.Day}";
        }

        static void AddState(Snapshot s, string fsm, string state) =>
            s.Fsms.Add(new Snapshot.FsmValue { Path = SunPath, Fsm = fsm, Kind = 's', Name = state });

        static void AddFloat(Snapshot s, string path, string fsm, string name, double value) =>
            s.Fsms.Add(new Snapshot.FsmValue { Path = path, Fsm = fsm, Kind = 'f', Name = name, Value = value });

        static string Hhmm(double minutes) => $"{(int)(minutes / 60):00}:{(int)(minutes % 60):00}";

        // ---- Washing ----

        public void OnWashed()
        {
            var dirtiness = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmFloat("PlayerDirtiness");
            if (dirtiness == null)
            {
                log("wash: MSC isn't in its game, PlayerWashed ignored");
                return;
            }
            float before = dirtiness.Value;
            dirtiness.Value = 0f;
            log($"wash: waded in up to the waist, dirtiness {before:0.#} -> 0");
        }

        // ---- Loads ----

        public void OnLoadReset()
        {
            var player = GameObject.Find("PLAYER");
            if (player == null)
            {
                log("load reset: MSC isn't in its game, nothing to stop");
                return;
            }
            var fps = player.transform.Find("Pivot/AnimPivot/Camera/FPSCamera");
            int moved = 0;
            if (fps != null)
            {
                moved += Reset(fps, LoadReset.Hands);
                moved += Reset(fps.Find("Piss"), LoadReset.Piss);
                moved += Reset(fps.Find("FPSCamera/Drink"), LoadReset.Drink);
                moved += Reset(fps.Find("FPSCamera"), LoadReset.Zoom);
                foreach (var t in fps.GetComponentsInChildren<Transform>(true))
                    if (t != fps && System.Array.IndexOf(LoadReset.Objects, t.name) >= 0 && t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        log($"load reset: {t.name} switched off");
                    }
                var smoking = Fsm(fps, LoadReset.Hands);
                var cigarette = smoking != null ? smoking.FsmVariables.FindFsmBool("Smoking") : null;
                if (cigarette != null) cigarette.Value = false;
            }
            moved += Reset(player.transform, LoadReset.Crouch);
            var globals = HutongGames.PlayMaker.FsmVariables.GlobalVariables;
            foreach (var name in LoadReset.Flags)
            {
                var b = globals.FindFsmBool(name);
                if (b != null) b.Value = false;
            }
            bool dropped = PocketLink.Carrying();
            if (dropped)
            {
                var hand = PocketLink.PickUp();
                foreach (var e in LoadReset.Drops) hand.SendEvent(e);
            }
            log($"load reset: {moved} FSMs back to idle, objects off, hands free{(dropped ? ", the carried item dropped" : "")}");
        }

        static PlayMakerFSM Fsm(Transform where, string name)
        {
            if (where == null) return null;
            foreach (var f in where.GetComponents<PlayMakerFSM>())
                if (f.FsmName == name) return f;
            return null;
        }

        // Sends the FSM named `name` on `where` to the idle or winding-down state its table gives.
        static int Reset(Transform where, string name)
        {
            var fsm = Fsm(where, name);
            if (fsm == null) return 0;
            var target = LoadReset.Target(name, fsm.ActiveStateName);
            if (target == null) return 0;
            var state = fsm.Fsm.GetState(target);
            if (state == null) return 0;
            SaveSync.SwitchState.Invoke(fsm.Fsm, new object[] { state });
            return 1;
        }
    }
}
