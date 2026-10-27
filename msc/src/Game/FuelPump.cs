using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's diesel pump at Teimo's, brought to the dwemer oil pump Kumpo is near, so taking the
    // nozzle, pouring and hanging it back are MSC's own. Its trigger (STORE/LOD/Pumps/GasolinePumpD/
    // Functions/TriggerD, FSM Use: PICK NOZZLE puts Pistol D in Kumpo's hand, which pours into a
    // vehicle's open cap; the nozzle drops further than 3 m from the trigger) is taken out of the
    // store's LOD, which switches off away from the store, and put in front of the boiler's gear box
    // at hand height; it goes back when Kumpo leaves. While it's here:
    //  - the store's opening hours, its pump switch and the police don't lock it (Check hand's USE
    //    goes straight to Activate hand instead of Check lock);
    //  - the hose reaches HoseReach instead of 3 m (Calculate drops the nozzle further than that from
    //    the camera), so a long vehicle like the Kekmet can be filled from beside the boiler;
    //  - Teimo's bill doesn't grow: the pump's Fuel and the register's DieselPurchase (Fuel times
    //    FuelPrice, set every frame while the nozzle is out) are held where they were. FuelLink takes
    //    the gold as the oil pours instead.
    public class FuelPump
    {
        const string TriggerPath = "STORE/LOD/Pumps/GasolinePumpD/Functions/TriggerD";
        const float DefaultPrice = 4f;  // marks a litre; MSC's FuelPrice was 3.999 (2026-10-27)
        const float HoseReach = 6f;     // meters from the trigger to the camera before the nozzle drops
        static readonly Vector3 HandSpot = new Vector3(0f, 1.35f, 0.45f);  // from the pump spot, facing the boiler (meters)

        readonly Action<string> log;
        Transform trigger;
        PlayMakerFSM use;
        FsmFloat pumped, purchase;
        FsmFloat hose;  // Calculate's drop distance, MSC's 3 m at the store
        float hoseHome;
        float pumpedHeld, purchaseHeld;
        Transform home;  // its parent at the store
        Vector3 homePosition;
        Quaternion homeRotation;
        bool homeActive, searched, placed;

        public FuelPump(Action<string> log)
        {
            this.log = log;
        }

        public float Price { get; private set; } = DefaultPrice;

        // at: the pump Kumpo is near, null for none.
        public void Update(Fuel.Pump? at, Island island)
        {
            if (!at.HasValue)
            {
                Away();
                return;
            }
            if (!Find()) return;
            var p = at.Value;
            var spot = island.SkyrimToIsland(p.Position);
            var facing = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(p.Heading), 0f);
            if (!placed)
            {
                placed = true;
                homeActive = trigger.gameObject.activeSelf;
                trigger.SetParent(null, true);
                trigger.gameObject.SetActive(true);
                Unlock(true);
                pumpedHeld = pumped != null ? pumped.Value : 0f;
                purchaseHeld = purchase != null ? purchase.Value : 0f;
                log($"fuel: MSC's diesel pump at the dwemer pump ({p.Position.x:0},{p.Position.y:0},{p.Position.z:0}), {Price:0.###} marks a litre");
            }
            trigger.position = new Vector3(spot.x, spot.y, spot.z) + facing * HandSpot;
            trigger.rotation = facing * Quaternion.Euler(0f, 180f, 0f);
            if (pumped != null) pumped.Value = pumpedHeld;
            if (purchase != null) purchase.Value = purchaseHeld;
        }

        public void Away()
        {
            if (!placed || trigger == null) return;
            placed = false;
            Unlock(false);
            trigger.SetParent(home, false);
            trigger.localPosition = homePosition;
            trigger.localRotation = homeRotation;
            trigger.gameObject.SetActive(homeActive);
            log("fuel: MSC's diesel pump back at the store");
        }

        void Unlock(bool unlock)
        {
            if (use == null) return;
            foreach (var state in use.FsmStates)
            {
                if (state.Name != "Check hand") continue;
                foreach (var t in state.Transitions)
                    if (t.EventName == "USE") t.ToState = unlock ? "Activate hand" : "Check lock";
            }
            if (hose != null) hose.Value = unlock ? HoseReach : hoseHome;
        }

        bool Find()
        {
            if (trigger != null) return true;
            if (searched) return false;
            searched = true;
            trigger = FindAnywhere(TriggerPath);
            if (trigger != null)
                foreach (var f in trigger.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Use") use = f;
            if (use == null)
            {
                trigger = null;
                log($"fuel: no {TriggerPath} :: Use, the dwemer pumps can't pour");
                return false;
            }
            home = trigger.parent;
            homePosition = trigger.localPosition;
            homeRotation = trigger.localRotation;
            var price = use.FsmVariables.FindFsmFloat("FuelPrice");
            if (price != null && price.Value > 0f) Price = price.Value;
            pumped = use.FsmVariables.FindFsmFloat("Fuel");
            hose = HoseLimit(use);
            if (hose != null) hoseHome = hose.Value;
            var register = use.FsmVariables.FindFsmGameObject("CashRegister");
            if (register != null && register.Value != null)
                foreach (var f in register.Value.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Data") purchase = f.FsmVariables.FindFsmFloat("DieselPurchase");
            log($"fuel: found MSC's diesel pump, {Price:0.###} marks a litre, register {(purchase != null ? "found" : "missing")}, hose {(hose != null ? $"{hoseHome:0.#} m" : "limit missing")}");
            return true;
        }

        // Calculate's FloatCompare of the trigger's distance to the camera against a constant.
        static FsmFloat HoseLimit(PlayMakerFSM fsm)
        {
            var state = Array.Find(fsm.FsmStates, st => st.Name == "Calculate");
            if (state == null) return null;
            foreach (var action in state.Actions)
            {
                if (action.GetType().Name != "FloatCompare") continue;
                var field = action.GetType().GetField("float2");
                var limit = field != null ? field.GetValue(action) as FsmFloat : null;
                if (limit != null && !limit.UseVariable) return limit;
            }
            return null;
        }

        // GameObject.Find skips inactive objects, and the store's LOD is off away from it.
        static Transform FindAnywhere(string path)
        {
            var found = GameObject.Find(path);
            if (found != null) return found.transform;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t.parent == null && path.StartsWith(t.name + "/", StringComparison.Ordinal))
                {
                    var child = t.Find(path.Substring(t.name.Length + 1));
                    if (child != null) return child;
                }
            return null;
        }
    }
}
