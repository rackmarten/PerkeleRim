using System;
using System.Collections.Generic;
using System.Reflection;
using Harmony;
using HutongGames.PlayMaker;

namespace PerkeleRim.Game
{
    // Watches what MSC's player drinks, smokes and eats while Skyrim has handed the player over, for
    // Skyrim to heal by (Consumed). A Harmony prefix on PlayMaker's Fsm.SwitchState, which every state
    // change goes through, hands each change to Consumption, which knows the FSMs and states.
    public static class ConsumeWatch
    {
        // Set every frame by PlayerLink: true while Skyrim has handed the player to MSC.
        public static bool Active;

        static bool installed;
        static Action<string> log;
        static readonly Queue<uint> pending = new Queue<uint>();

        public static void Install(Action<string> log)
        {
            ConsumeWatch.log = log;
            if (installed) return;
            installed = true;
            var target = typeof(Fsm).GetMethod("SwitchState", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(FsmState) }, null);
            if (target == null) { log("consume: Fsm.SwitchState not found, drinking, smoking and eating don't heal"); return; }
            try
            {
                var harmony = HarmonyInstance.Create("PerkeleRim.ConsumeWatch");
                harmony.Patch(target, new HarmonyMethod(typeof(ConsumeWatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)), null);
                log("consume: drinking, smoking and eating are watched while Skyrim has handed the player over");
            }
            catch (Exception e) { log("consume: patching Fsm.SwitchState FAILED: " + e.Message); }
        }

        static void Prefix(Fsm __instance, FsmState toState)
        {
            if (!Active || toState == null) return;
            string name = __instance.Name;
            if (name != "Drink" && name != "PlayerFunctions" && name != "Use") return;
            var from = __instance.ActiveState;
            uint kind = Consumption.Kind(name, from != null ? from.Name : null, toState.Name, name == "Use" && HasState(__instance, "Check drink"));
            if (kind == 0) return;
            log($"consume: {__instance.GameObjectName} :: {name} {from.Name} -> {toState.Name}, kind {kind}");
            pending.Enqueue(kind);
        }

        static bool HasState(Fsm fsm, string state)
        {
            foreach (var s in fsm.States)
                if (s.Name == state) return true;
            return false;
        }

        // The oldest consume not sent yet. Sent() drops it once the link took it.
        public static bool Peek(out uint kind)
        {
            kind = pending.Count > 0 ? pending.Peek() : 0;
            return pending.Count > 0;
        }

        public static void Sent() { if (pending.Count > 0) pending.Dequeue(); }

        // Skyrim took the player back: what wasn't sent heals nothing.
        public static void Clear() => pending.Clear();
    }
}
