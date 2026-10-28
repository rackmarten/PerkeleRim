using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Fuel along Skyrim's roads (docs/PROTOCOL.md, "Fuel"; rules in msc/src/Link/Fuel.cs). The van,
    // the Kekmet and the Gifu burn MSC's diesel as MSC burns it; Skyrim sends the dwemer oil pumps
    // (FuelStations) and this:
    //  - brings MSC's diesel pump to the one Kumpo is near (FuelPump), so the hatch, the cap, the
    //    nozzle and pouring are MSC's own;
    //  - takes gold (MSC's PlayerMoney, which Skyrim's gold follows) for every litre a tank gains
    //    while Kumpo stands at a pump, as it pours, and takes back what the money doesn't cover;
    //  - tows a dry vehicle called near a pump onto it for Fuel.TowFee (free when it stands within
    //    100 m of the pump), with its parking brake on, and refuses the call anywhere else;
    //  - sends the HUD's fuel line (FuelText).
    public class FuelLink
    {
        const float PayReach = 1000f;    // units (14 m): a tank filling this close to a pump is paid for
        const float PourShown = 6f;      // seconds the litres stay on the HUD after the last drop
        const float FlashShown = 5f;     // seconds a message (dry, towed, out of gold) stays up
        const float MinPour = 0.001f;    // litres a frame below which a tank didn't fill

        readonly Island island;
        readonly Action<string> log;
        readonly FuelPump pump;
        readonly List<Fuel.Pump> pumps = new List<Fuel.Pump>();
        readonly Dictionary<VehicleLink, Baseline> last = new Dictionary<VehicleLink, Baseline>();
        uint generation;
        float poured, paid;   // this fill's litres and marks
        VehicleLink filling;  // the vehicle this fill pours into
        float lastPour = float.NegativeInfinity;
        int pourPump = -1;
        string flash;
        float flashUntil;
        string sent;
        FsmFloat money;
        int loggedLitre = -1;  // the driven tank's whole litres last logged

        struct Baseline
        {
            public float Level;
            public int Moves;
        }

        public FuelLink(Island island, Action<string> log)
        {
            this.island = island;
            this.log = log;
            pump = new FuelPump(log);
        }

        public void OnStations(byte[] payload, int size)
        {
            List<Fuel.Pump> got;
            if (!Fuel.Decode(payload, size, out got))
            {
                log($"fuel: bad FuelStations ({size} bytes)");
                return;
            }
            pumps.Clear();
            pumps.AddRange(got);
            log($"fuel: {pumps.Count} pumps");
        }

        // Every LateUpdate, after the vehicles. playing: MSC owns the player and it stands on the
        // current island.
        public void Update(Link link, bool playing, Vector3 feet, VehicleLink driven, VehicleLink[] vehicles)
        {
            if (!playing)
            {
                last.Clear();
                pump.Away();
                Send(link, playing, "");
                return;
            }
            if (generation != island.Generation)
            {
                generation = island.Generation;
                last.Clear();  // a new anchor: snapshots and moves may have set the tanks
            }
            var at = island.IslandToSkyrim(new Vec3(feet.x, feet.y, feet.z));
            int near = Fuel.Nearest(pumps, at, PayReach);
            pump.Update(near >= 0 ? (Fuel.Pump?)pumps[near] : null, island);
            if (near != pourPump)
            {
                pourPump = near;
                poured = paid = 0f;  // a new pump, or away from them: a new fill
            }

            foreach (var v in vehicles)
            {
                float level, capacity;
                if (v.EndlessFuel || !v.Tank(out level, out capacity)) continue;
                Baseline before;
                bool known = last.TryGetValue(v, out before) && before.Moves == v.Moves;
                last[v] = new Baseline { Level = level, Moves = v.Moves };
                if (!known || near < 0) continue;  // moved by code, or filled away from a pump (free)
                float gained = level - before.Level;
                if (gained < MinPour) continue;
                Pay(v, gained, level);
            }

            LogBurn(driven);
            Send(link, playing, Line(driven));
        }

        // The driven tank's level in the log at every whole litre it burns, to check consumption.
        void LogBurn(VehicleLink driven)
        {
            float level, capacity;
            if (driven == null || driven.EndlessFuel || !driven.Tank(out level, out capacity))
            {
                loggedLitre = -1;
                return;
            }
            int litre = Mathf.FloorToInt(level);
            if (litre == loggedLitre) return;
            if (loggedLitre < 0 || litre < loggedLitre) log($"fuel: the {driven.Name} has {level:0.0} of {capacity:0} L");
            loggedLitre = litre;
        }

        void Pay(VehicleLink v, float gained, float level)
        {
            if (money == null) money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            if (money == null) return;
            float price = pump.Price;
            float cash = money.Value;
            float covered = Fuel.Pay(gained, price, ref cash);
            money.Value = cash;
            poured += covered;
            paid += covered * price;
            filling = v;
            lastPour = Time.realtimeSinceStartup;
            if (covered < gained)
            {
                float kept = level - (gained - covered);
                v.SetFuel(kept);
                last[v] = new Baseline { Level = kept, Moves = v.Moves };
                if (flash != Fuel.BrokeLine) log($"fuel: out of money at the pump, the {v.Name} got {poured:0.0} L for {paid:0.00} marks");
                Flash(Fuel.BrokeLine);
            }
        }

        // The call keys ([, ], ;): a vehicle with fuel comes as before; a dry one is towed onto the
        // pump Kumpo stands near, for the fee, or stays where it is.
        public void Call(VehicleLink v, Transform player, Vector3 feet)
        {
            float level, capacity;
            if (v.EndlessFuel || !v.Tank(out level, out capacity) || !Fuel.Dry(level))
            {
                v.Summon(player, feet);
                return;
            }
            var at = island.IslandToSkyrim(new Vec3(feet.x, feet.y, feet.z));
            int near = Fuel.Nearest(pumps, at, Fuel.TowReach);
            if (near < 0)
            {
                log($"fuel: the {v.Name} is dry, no pump within {Fuel.TowReach / 70f:0} m, not calling it");
                Flash(Fuel.DryLine);
                return;
            }
            var vehicleAt = v.Position;
            float fee = Fuel.TowFeeFrom(island.IslandToSkyrim(new Vec3(vehicleAt.x, vehicleAt.y, vehicleAt.z)), pumps[near]);
            if (money == null) money = FsmVariables.GlobalVariables.FindFsmFloat("PlayerMoney");
            if (money == null || !Fuel.CanTow(money.Value, fee))
            {
                log($"fuel: the {v.Name} is dry and towing costs {fee:0}, the player has {(money != null ? money.Value : 0f):0.00}");
                Flash(Fuel.NoTowMoneyLine);
                return;
            }
            int side;
            float forward;
            v.Filler(out side, out forward);
            Vec3 spot;
            float heading;
            Fuel.TowSpot(pumps[near], side, forward, out spot, out heading);
            money.Value -= fee;
            v.TowTo(player, spot, heading);
            last.Remove(v);
            Flash(Fuel.TowedLine(fee));
        }

        void Flash(string line)
        {
            flash = line;
            flashUntil = Time.realtimeSinceStartup + FlashShown;
        }

        string Line(VehicleLink driven)
        {
            float now = Time.realtimeSinceStartup;
            if (flash != null && now < flashUntil) return flash;
            float level, capacity;
            if (poured > 0f && now - lastPour < PourShown && filling != null && filling.Tank(out level, out capacity))
                return Fuel.PourLine(poured, paid, level, capacity);
            if (driven != null && !driven.EndlessFuel && driven.Tank(out level, out capacity) && Fuel.Low(level, capacity))
                return Fuel.LowLine(level, capacity);
            return "";
        }

        void Send(Link link, bool playing, string text)
        {
            if (!playing && sent == null) return;
            if (text == sent) return;
            var payload = LookAt.Encode(text);
            if (link.Send(MessageType.FuelText, payload, payload.Length)) sent = playing ? text : null;
        }
    }
}
