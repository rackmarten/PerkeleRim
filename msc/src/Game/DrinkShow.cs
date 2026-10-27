using System;
using HutongGames.PlayMaker;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // MSC's own drinking for a drink Kumpo had in Skyrim (ConsumeEvent.drink, rules in
    // Link/Consumption.cs, docs/FSM_MAP.md "Drinking"): the player's Drink FSM gets the event for that
    // bottle. DRINKBEER and DRINKVIINA are global transitions, the others work from the idle State 4
    // only; PocketLink sends one only while no drink is going, so all go to the FSM itself.
    // MSC's spirit blinds Kumpo one time in five (a BLIND transition on to its METHANOL event). For a
    // spirit from Skyrim (Skooma) every BLIND transition is pointed at its state's other way out
    // until Restore.
    static class DrinkShow
    {
        const string DrinkPath = "PLAYER/Pivot/AnimPivot/Camera/FPSCamera/FPSCamera/Drink";
        const string Blind = "BLIND";

        static FsmTransition[] blocked = new FsmTransition[0];
        static string[] blockedTo = new string[0];

        static PlayMakerFSM Fsm()
        {
            var go = GameObject.Find(DrinkPath);
            if (go == null) return null;
            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Drink") return fsm;
            return null;
        }

        // Starts MSC's drinking for drink (Wire.Drink*); false when there is no such drink or no FSM.
        public static bool Start(uint drink, Action<string> log)
        {
            var name = Consumption.DrinkEvent(drink);
            var fsm = Fsm();
            if (name == null || fsm == null) { log($"drink: no Drink FSM or no event for drink {drink}"); return false; }
            if (drink == Wire.DrinkSpirit) BlockBlindness(fsm, log);
            fsm.SendEvent(name);
            log($"drink: {name} sent (Drink was in {fsm.ActiveStateName})");
            return true;
        }

        static void BlockBlindness(PlayMakerFSM fsm, Action<string> log)
        {
            Restore(log);
            var found = new System.Collections.Generic.List<FsmTransition>();
            var to = new System.Collections.Generic.List<string>();
            foreach (var state in fsm.FsmStates)
            {
                string safe = null;
                foreach (var t in state.Transitions ?? new FsmTransition[0])
                    if (t.EventName != Blind && !string.IsNullOrEmpty(t.ToState)) { safe = t.ToState; break; }
                foreach (var t in state.Transitions ?? new FsmTransition[0])
                {
                    if (t.EventName != Blind) continue;
                    if (safe == null) { log($"drink: {state.Name}'s BLIND has no other way out, left alone"); continue; }
                    log($"drink: {state.Name}'s BLIND goes to {safe} instead of {t.ToState} while it's a drink from Skyrim");
                    found.Add(t);
                    to.Add(t.ToState);
                    t.ToState = safe;
                }
            }
            if (found.Count == 0) log("drink: the spirit has no BLIND transition to block (it can still blind)");
            blocked = found.ToArray();
            blockedTo = to.ToArray();
        }

        // The spirit's BLIND transitions back as MSC made them, once a shown drink is down.
        public static void Restore(Action<string> log)
        {
            if (blocked.Length == 0) return;
            for (int i = 0; i < blocked.Length; i++) blocked[i].ToState = blockedTo[i];
            log($"drink: the spirit's BLIND back to MSC's ({blocked.Length})");
            blocked = new FsmTransition[0];
            blockedTo = new string[0];
        }
    }
}
